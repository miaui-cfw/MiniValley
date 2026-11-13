// Lists every ContentTypeReader type string used by the game's .xnb files (decompressing LZX/LZ4 with MonoGame's own decoders).
using System.Reflection;
var mg = typeof(Microsoft.Xna.Framework.Game).Assembly;
var lzx = mg.GetTypes().First(t => t.Name == "LzxDecoderStream");
var lz4 = mg.GetTypes().FirstOrDefault(t => t.Name == "Lz4DecoderStream");
Console.Error.WriteLine($"lzx ctor: {string.Join(" | ", lzx.GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Select(c => string.Join(",", c.GetParameters().Select(p => p.ParameterType.Name))))}");
var readers = new SortedDictionary<string, int>(StringComparer.Ordinal);
int files = 0, failed = 0;
foreach (var path in Directory.EnumerateFiles(args[0], "*.xnb", SearchOption.AllDirectories)) {
    try {
        using var fs = File.OpenRead(path); var br = new BinaryReader(fs);
        if (new string(br.ReadChars(3)) != "XNB") continue;
        br.ReadByte(); br.ReadByte(); byte flags = br.ReadByte(); int size = br.ReadInt32();
        Stream s = fs;
        if ((flags & 0x80) != 0) { int dsize = br.ReadInt32(); s = (Stream)Activator.CreateInstance(lzx, BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic, null, new object[] { fs, dsize, size - 14 }, null); }
        else if ((flags & 0x40) != 0) { int dsize = br.ReadInt32(); s = (Stream)Activator.CreateInstance(lz4, BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic, null, new object[] { fs, null, true }, null); }
        var r = new BinaryReader(s);
        int n = r.Read7BitEncodedInt();
        for (int i = 0; i < n; i++) { var name = r.ReadString(); r.ReadInt32(); readers[name] = readers.GetValueOrDefault(name) + 1; }
        files++;
    } catch (Exception e) { failed++; if (failed < 5) Console.Error.WriteLine(path + ": " + e.Message); }
}
Console.Error.WriteLine($"{files} files, {failed} failed, {readers.Count} distinct reader strings");
foreach (var kv in readers) Console.WriteLine(kv.Key);
