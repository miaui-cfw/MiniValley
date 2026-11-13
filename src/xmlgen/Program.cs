// Generates C# XmlSerializer code (sgen-style) for every serializer the game creates, so NativeAOT never falls back
// to reflection-mode XmlSerializer. Output: one .cs file with readers/writers + a typed serializer per root type.
using System.Reflection;
using System.Xml.Serialization;
using StardewValley;

AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var n = new AssemblyName(e.Name).Name;
    foreach (var dir in new[] { "/w/libs", "/game" }) { var p = Path.Combine(dir, n + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
    return null;
};
var game = typeof(SaveGame).Assembly;

// Root types: the explicit serializers, Item (Inventory), and every key/value type of a SerializableDictionary
// (SerializableDictionary.ReadXml/WriteXml create SaveGame.GetSerializer(typeof(TKey)) / (typeof(TValue))).
var roots = new List<Type> { typeof(SaveGame), typeof(Farmer), typeof(GameLocation), typeof(StartupPreferences), typeof(Options),
                             typeof(StardewValley.Quests.DescriptionElement), typeof(Item) };
var legacy = game.GetType("StardewValley.SaveMigrations.LegacyDescriptionElement");
if (legacy != null) roots.Add(legacy);
var dictArgs = new HashSet<Type>();
var seen = new HashSet<Type>();
void Walk(Type t)
{
    if (t == null || t.IsGenericParameter || t.ContainsGenericParameters || !seen.Add(t)) return;
    if (t.IsArray) { Walk(t.GetElementType()); return; }
    for (var b = t; b != null; b = b.BaseType)
        if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(SerializableDictionary<,>))
            foreach (var a in b.GetGenericArguments()) dictArgs.Add(a);
    if (t.IsGenericType) foreach (var a in t.GetGenericArguments()) Walk(a);
    if (t.IsGenericType) Walk(t.BaseType);   // e.g. NetLongDictionary<FarmAnimal,..> : NetDictionary<.., SerializableDictionary<long, FarmAnimal>, ..>
}
const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
foreach (var t in game.GetTypes())
{
    if (t.ContainsGenericParameters) continue;
    Walk(t); Walk(t.BaseType);
    foreach (var f in t.GetFields(F)) Walk(f.FieldType);
    foreach (var p in t.GetProperties(F)) Walk(p.PropertyType);
}
foreach (var a in dictArgs.OrderBy(x => x.FullName)) if (!roots.Contains(a)) roots.Add(a);
// seen as reflection fallbacks at runtime ([xml] lines): Inventory.ReadXml, item lists, dictionary values
foreach (var extra in new[] { typeof(StardewValley.Inventories.Inventory), typeof(List<Item>), typeof(StardewValley.Object), typeof(FarmAnimal),
                              typeof(StardewValley.TerrainFeatures.TerrainFeature), typeof(Microsoft.Xna.Framework.Vector2), typeof(SerializableDictionary<string, int>) })
    if (!roots.Contains(extra)) roots.Add(extra);

XmlReflectionImporter NewImporter()
{
    // the union of the game's extra types (each original serializer used a subset)
    var imp = new XmlReflectionImporter();
    foreach (var extra in new[] { typeof(Character), typeof(GameLocation), typeof(Item), typeof(StardewValley.Quests.Quest), typeof(StardewValley.TerrainFeatures.TerrainFeature) })
        imp.IncludeType(extra);
    return imp;
}
// XmlSerializer handles primitives itself (no mappings, no reflection): skip them.
static bool Known(Type t) => t.IsPrimitive || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
    || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[]) || t == typeof(char) || t == typeof(System.Xml.XmlQualifiedName);
var gen = typeof(XmlSerializer).GetMethod("GenerateSerializer", BindingFlags.Static | BindingFlags.NonPublic, new[] { typeof(Type[]), typeof(XmlMapping[]), typeof(Stream) })
          ?? throw new Exception("XmlSerializer.GenerateSerializer(Type[], XmlMapping[], Stream) not found");
// Groups: the game's plain types share one importer (one file); every generic, array or other-assembly root gets its own
// importer and file (a shared importer merges mappings with the same XML type name: int[] vs NetIntDelta).
var groups = new List<(string tag, List<Type> types)>();
var main = roots.Where(t => !Known(t) && t.Assembly == game && !t.IsGenericType && !t.IsArray).ToList();
groups.Add(("StardewValley", main));
int k = 0;
foreach (var t in roots.Where(t => !Known(t) && !main.Contains(t)))
    groups.Add(($"G{k++}_" + new string(t.Name.Where(char.IsLetterOrDigit).ToArray()), new List<Type> { t }));
var all = new List<string>();
foreach (var (tag, group) in groups)
{
    var importer = NewImporter();
    var types = new List<Type>(); var mappings = new List<XmlMapping>();
    foreach (var t in group)
    {
        try { mappings.Add(importer.ImportTypeMapping(t)); types.Add(t); }
        catch (Exception e) { Console.Error.WriteLine($"skip {t}: {e.GetBaseException().Message}"); }
    }
    if (types.Count == 0) continue;
    string file = Path.Combine(args[0], $"XmlSerializers.{tag}.g.cs");
    using (var ms = new MemoryStream())
    {
        var ok = (bool)gen.Invoke(null, new object[] { types.ToArray(), mappings.ToArray(), ms });
        string code = System.Text.Encoding.UTF8.GetString(ms.ToArray())
            .Replace("namespace Microsoft.Xml.Serialization.GeneratedAssembly", "namespace SdvAot.Xml." + tag);
        // assembly-level attributes (version/security) would clash between files and aren't used: drop them
        code = string.Join("\n", code.Split('\n').Where(l => !l.TrimStart().StartsWith("[assembly:")));
        File.WriteAllText(file, code);
        Console.Error.WriteLine($"{tag}: generated={ok}, {types.Count} types, {code.Length / 1024} KB");
    }
    all.AddRange(types.Select(t => $"{tag}\t{t.AssemblyQualifiedName}"));
}
File.WriteAllLines(Path.Combine(args[0], "roots.txt"), all);
var tags = all.Select(l => l.Split('\t')[0]).Distinct().ToList();
// The generated contracts leave generic types out of CanSerialize/GetSerializer: single-type groups get an explicit entry
// (type -> its typed serializer class) so the factory finds them too.
static string Cs(Type t) => t.IsArray ? Cs(t.GetElementType()) + "[]"
    : t.IsGenericType ? "global::" + t.GetGenericTypeDefinition().FullName.Split('`')[0].Replace('+', '.') + "<" + string.Join(", ", t.GetGenericArguments().Select(Cs)) + ">"
    : "global::" + t.FullName.Replace('+', '.');
var extraEntries = new List<string>();
foreach (var (tag, group) in groups.Where(g => g.types.Count == 1 && (g.types[0].IsGenericType || g.types[0].IsArray)))
{
    string file = Path.Combine(args[0], $"XmlSerializers.{tag}.g.cs");
    if (!File.Exists(file)) continue;
    var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(file), @"public sealed class (\w+) : XmlSerializer1");
    if (m.Success) extraEntries.Add($"        (typeof({Cs(group[0])}), () => new SdvAot.Xml.{tag}.{m.Groups[1].Value}()),\n");
}
File.WriteAllText(Path.Combine(args[0], "Contracts.g.cs"), "namespace SdvAot.Xml;\ninternal static class Contracts\n{\n    internal static readonly System.Xml.Serialization.XmlSerializerImplementation[] All =\n    {\n"
    + string.Concat(tags.Select(t => $"        new SdvAot.Xml.{t}.XmlSerializerContract(),\n")) + "    };\n\n"
    + "    internal static readonly (System.Type type, System.Func<System.Xml.Serialization.XmlSerializer> create)[] Generic =\n    {\n"
    + string.Concat(extraEntries) + "    };\n}\n");
