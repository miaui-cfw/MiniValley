// readers.txt -> rd.xml: every reader instantiation the content uses, so NativeAOT compiles them ahead of time.
using System.Reflection;
using System.Security;
AppDomain.CurrentDomain.AssemblyResolve += (_, e) => { var p = $"/game/{new AssemblyName(e.Name).Name}.dll"; return File.Exists(p) ? Assembly.LoadFrom(p) : null; };
var mgr = typeof(Microsoft.Xna.Framework.Content.ContentTypeReaderManager);
var prep = mgr.GetMethod("PrepareType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
string Name(Type t) => t.IsGenericType
    ? t.GetGenericTypeDefinition().FullName + "[" + string.Join(",", t.GetGenericArguments().Select(a => "[" + Name(a) + "," + a.Assembly.GetName().Name + "]")) + "]"
    : t.IsArray ? Name(t.GetElementType()) + "[]" : t.FullName;
var byAsm = new SortedDictionary<string, SortedSet<string>>();
void Add(Type t) { var a = t.Assembly.GetName().Name; if (!byAsm.TryGetValue(a, out var s)) byAsm[a] = s = new SortedSet<string>(StringComparer.Ordinal); s.Add(Name(t)); }
int bad = 0;
if (args[0] == "--fields") {
    // every closed generic type (and array) used by a field or property of the game assemblies
    var seen = new HashSet<Type>();
    void Walk(Type t) {
        if (t == null || t.IsGenericParameter || t.ContainsGenericParameters || t.IsPointer || t.IsByRef || !seen.Add(t)) return;
        if (t.IsArray) { Walk(t.GetElementType()); return; }
        if (t.IsGenericType) { Add(t); foreach (var a in t.GetGenericArguments()) Walk(a); }
    }
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    foreach (var name in args.Skip(2)) {
        var asm = Assembly.LoadFrom(name);
        Type[] types; try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
        foreach (var t in types) {
            if (t.ContainsGenericParameters) continue;
            for (var b = t.BaseType; b != null; b = b.BaseType) Walk(b);
            foreach (var f in t.GetFields(F)) Walk(f.FieldType);
            foreach (var pr in t.GetProperties(F)) Walk(pr.PropertyType);
        }
    }
} else
foreach (var line in File.ReadLines(args[0])) {
    var s = (string)prep.Invoke(null, new object[] { line });
    var t = Type.GetType(s, an => { try { return Assembly.Load(an); } catch { var p = $"/game/{an.Name}.dll"; return File.Exists(p) ? Assembly.LoadFrom(p) : null; } },
                         (asm, name, ic) => (asm ?? mgr.Assembly).GetType(name, false, ic) ?? (asm == null ? Type.GetType(name, false, ic) : null));
    if (t == null) { Console.Error.WriteLine("UNRESOLVED " + line); bad++; continue; }
    Add(t);
    // the reader's target type and generic arguments, which readers construct at load time
    foreach (var ga in t.IsGenericType ? t.GetGenericArguments() : Type.EmptyTypes) if (ga.IsGenericType || ga.IsArray) Add(ga);
}
using var w = new StreamWriter(args[1]);
w.WriteLine("<Directives xmlns=\"http://schemas.microsoft.com/netfx/2013/01/metadata\"><Application>");
foreach (var (asm, types) in byAsm) {
    w.WriteLine($"  <Assembly Name=\"{asm}\">");
    foreach (var t in types) w.WriteLine($"    <Type Name=\"{SecurityElement.Escape(t)}\" Dynamic=\"Required All\" />");
    w.WriteLine("  </Assembly>");
}
w.WriteLine("</Application></Directives>");
Console.Error.WriteLine($"{byAsm.Values.Sum(s => s.Count)} types in {byAsm.Count} assemblies, {bad} unresolved");
