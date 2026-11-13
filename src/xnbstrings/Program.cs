// Overrides strings in the game's Dictionary<string,string> .xnb assets (Strings/UI and the like) from a JSON file.
// Reads the pristine Steam files, so every run starts from the originals, and writes uncompressed .xnb files
// (MonoGame loads them as they are). LZX is decoded with MonoGame's own LzxDecoderStream, as xnbscan does.
//
// Usage (inside the minivalley-build image, the game mounted at /game; build-pak.sh --strings runs it): XnbStrings <strings.json> <source Content> <target Content>
using System.Reflection;
using System.Text.Json;

/// <summary>Entry point and XNB string-dictionary patcher.</summary>
static class Program
{
    /// <summary>The JSON's name for the unsuffixed (English) asset file.</summary>
    const string BaseLanguage = "en";

    /// <summary>One key/value pair of the dictionary, with the type ids the XNB stores before each string.</summary>
    /// <param name="KeyType">Reader index of the key (0 = null).</param>
    /// <param name="Key">The translation key.</param>
    /// <param name="ValueType">Reader index of the value (0 = null).</param>
    /// <param name="Value">The translated text, or null.</param>
    record struct Entry(int KeyType, string Key, int ValueType, string Value);

    /// <summary>A decoded Dictionary&lt;string,string&gt; asset.</summary>
    /// <param name="Flags">XNB header flags (bit 0x80 = LZX, 0x40 = LZ4).</param>
    /// <param name="Head">Decompressed bytes before the entry count: reader list, shared count, root type id.</param>
    /// <param name="Entries">The dictionary, in file order.</param>
    /// <param name="Tail">Decompressed bytes after the entries (shared resources), copied unchanged.</param>
    record Asset(byte Flags, byte[] Head, List<Entry> Entries, byte[] Tail);

    /// <summary>Applies every override in the JSON and reports what changed.</summary>
    /// <param name="args">JSON path, source Content folder, target Content folder.</param>
    /// <returns>0 on success, 1 on any error (missing file or key, shadowing key).</returns>
    static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: XnbStrings <strings.json> <source Content> <target Content>");
            return 1;
        }
        // asset ("Strings/UI") -> key ("ExitToDesktop") -> language ("en", "es-ES", ...) -> text
        var overrides = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(File.ReadAllText(args[0]));
        string src = args[1], dst = args[2];
        int errors = 0;
        foreach (var (asset, keys) in overrides)
        {
            var languages = keys.Values.SelectMany(l => l.Keys).Distinct().ToList();
            foreach (var language in languages)
            {
                string rel = asset + (language == BaseLanguage ? "" : "." + language) + ".xnb";
                string from = Path.Combine(src, rel), to = Path.Combine(dst, rel);
                if (!File.Exists(from) || !File.Exists(to))
                {
                    Console.Error.WriteLine($"error: {rel} is missing in {(File.Exists(from) ? dst : src)}");
                    errors++;
                    continue;
                }
                var xnb = Read(from);
                foreach (var (key, texts) in keys)
                {
                    if (!texts.TryGetValue(language, out var text))
                        continue;
                    // LocalizedContentManager.GetString reads "<key>.desktop" first: an override there would be hidden
                    if (xnb.Entries.Any(e => e.Key == key + ".desktop"))
                    {
                        Console.Error.WriteLine($"error: {rel} has {key}.desktop, which the game reads instead of {key}");
                        errors++;
                        continue;
                    }
                    int i = xnb.Entries.FindIndex(e => e.Key == key);
                    if (i < 0)
                    {
                        Console.Error.WriteLine($"error: {rel} has no key {key}");
                        errors++;
                        continue;
                    }
                    Console.WriteLine($"{rel} {key}: \"{xnb.Entries[i].Value}\" -> \"{text}\"");
                    xnb.Entries[i] = xnb.Entries[i] with { ValueType = xnb.Entries[i].KeyType, Value = text }; // both use the StringReader
                }
                Write(to, xnb);
            }
            // shipped languages the JSON leaves out keep the game's text: say so
            string dir = Path.Combine(dst, Path.GetDirectoryName(asset)), name = Path.GetFileName(asset);
            foreach (var file in Directory.EnumerateFiles(dir, name + ".*xnb"))
            {
                var parts = Path.GetFileName(file).Split('.');
                string language = parts.Length == 3 ? parts[1] : BaseLanguage;
                if (parts[0] == name && !languages.Contains(language))
                    Console.WriteLine($"warning: {asset} {language} is shipped but has no overrides");
            }
        }
        return errors == 0 ? 0 : 1;
    }

    /// <summary>Reads and decompresses a Dictionary&lt;string,string&gt; .xnb file.</summary>
    /// <param name="path">The .xnb file (LZX-compressed or uncompressed).</param>
    /// <returns>The decoded asset.</returns>
    static Asset Read(string path)
    {
        using var fs = File.OpenRead(path);
        var br = new BinaryReader(fs);
        br.ReadBytes(5); // "XNB", platform, version
        byte flags = br.ReadByte();
        int size = br.ReadInt32();
        byte[] data;
        if ((flags & 0x80) != 0)
        {
            int dsize = br.ReadInt32();
            var lzx = typeof(Microsoft.Xna.Framework.Game).Assembly.GetTypes().First(t => t.Name == "LzxDecoderStream");
            var s = (Stream)Activator.CreateInstance(lzx, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { fs, dsize, size - 14 }, null);
            data = new BinaryReader(s).ReadBytes(dsize);
        }
        else
            data = br.ReadBytes(size - 10);

        var r = new BinaryReader(new MemoryStream(data));
        int readers = r.Read7BitEncodedInt();
        for (int i = 0; i < readers; i++)
        {
            string reader = r.ReadString();
            r.ReadInt32();
            if (i == 0 && !reader.StartsWith("Microsoft.Xna.Framework.Content.DictionaryReader`2[[System.String,"))
                throw new InvalidDataException($"{path}: not a string dictionary ({reader})");
        }
        r.Read7BitEncodedInt(); // shared resources
        r.Read7BitEncodedInt(); // root object's reader
        var head = data[..(int)r.BaseStream.Position];
        int count = r.ReadInt32();
        var entries = new List<Entry>(count);
        for (int i = 0; i < count; i++)
        {
            int kt = r.Read7BitEncodedInt();
            string k = kt == 0 ? null : r.ReadString();
            int vt = r.Read7BitEncodedInt();
            string v = vt == 0 ? null : r.ReadString();
            entries.Add(new Entry(kt, k, vt, v));
        }
        return new Asset(flags, head, entries, data[(int)r.BaseStream.Position..]);
    }

    /// <summary>Writes an asset as an uncompressed .xnb file.</summary>
    /// <param name="path">Destination file, replaced.</param>
    /// <param name="xnb">The asset to write.</param>
    static void Write(string path, Asset xnb)
    {
        var body = new MemoryStream();
        var w = new BinaryWriter(body);
        w.Write(xnb.Head);
        w.Write(xnb.Entries.Count);
        foreach (var e in xnb.Entries)
        {
            w.Write7BitEncodedInt(e.KeyType);
            if (e.KeyType != 0)
                w.Write(e.Key);
            w.Write7BitEncodedInt(e.ValueType);
            if (e.ValueType != 0)
                w.Write(e.Value);
        }
        w.Write(xnb.Tail);

        using var fs = File.Create(path);
        var o = new BinaryWriter(fs);
        o.Write("XNBw"u8);
        o.Write((byte)5);
        o.Write((byte)(xnb.Flags & ~0xC0)); // no compression; the HiDef bit stays
        o.Write((int)(10 + body.Length));
        body.Position = 0;
        body.CopyTo(fs);
    }
}
