using System;
using System.Collections.Generic;
using System.Xml.Serialization;
namespace SdvAot.Xml;

/// <summary>Every "new XmlSerializer(...)" in the game is redirected here (patcher mode xmlnew). Types covered by the
/// build-time generated code get a typed serializer (compiled readers/writers, no reflection); anything else falls back
/// to a normal XmlSerializer and is logged so the generator's root list can be extended.</summary>
public static class Factory
{
    static readonly XmlSerializerImplementation[] contracts = Contracts.All;   // generated, one per assembly group
    static readonly Dictionary<Type, XmlSerializer> cache = new();

    public static XmlSerializer Create(Type type) => Create(type, null);

    public static XmlSerializer Create(Type type, Type[] extraTypes)
    {
        lock (cache)
        {
            if (cache.TryGetValue(type, out var s)) return s;
            foreach (var c in contracts)
                if (c.CanSerialize(type)) { s = c.GetSerializer(type); break; }
            if (s == null)
                foreach (var (t, create) in Contracts.Generic)
                    if (t == type) { s = create(); break; }
            if (s == null)
            {
                bool primitive = type.IsPrimitive || type == typeof(string);
                if (!primitive) Console.Error.WriteLine($"[xml] reflection fallback for {type}");
                s = extraTypes == null ? new XmlSerializer(type) : new XmlSerializer(type, extraTypes);
            }
            cache[type] = s;
            return s;
        }
    }
}
