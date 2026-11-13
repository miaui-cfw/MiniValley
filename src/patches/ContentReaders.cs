using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework.Content;
namespace SdvAot;

/// <summary>MonoGame resolves each .xnb reader by its type string with Type.GetType. Under NativeAOT the "mscorlib"
/// facade is absent, and the generic instantiations must exist ahead of time (readers.rd.xml compiles them).
/// Register a creator per string so the loader never needs the facade.</summary>
public static class ContentReaders
{
    public static int Register()
    {
        var mg = typeof(ContentManager).Assembly;
        int n = 0;
        foreach (var original in ReaderList.All)
        {
            string s = original;
            // same cleanup as MonoGame's PrepareType, plus mscorlib -> System.Private.CoreLib
            s = Regex.Replace(s, @", Version=[^,\]]+, Culture=[^,\]]+, PublicKeyToken=[^,\]]+", "");
            s = s.Replace(", mscorlib", ", System.Private.CoreLib");
            s = Regex.Replace(s, @", Microsoft\.Xna\.Framework(\.[A-Za-z]+)?\b", ", MonoGame.Framework");
            var t = Type.GetType(s, false) ?? mg.GetType(s, false);
            if (t == null) { Console.Error.WriteLine("[aot] reader type not found: " + s); continue; }
            ContentTypeReaderManager.AddTypeCreator(original, () => (ContentTypeReader)Activator.CreateInstance(t, true));
            n++;
        }
        return n;
    }
}
