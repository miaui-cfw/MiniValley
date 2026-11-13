using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace SdvAot;

/// <summary>Reflection-mode XmlSerializer (the only mode under NativeAOT) finds a collection's Add with
/// Type.GetMethod("Add"), which throws on types with several Add overloads (Netcode's NetDictionary has three).
/// The patched System.Private.Xml calls this instead: pick the one-parameter Add that takes the element type,
/// as the emitted serializer does.</summary>
public static class XmlFix
{
    public static MethodInfo GetMethod(Type type, string name)
    {
        lock (cache) if (cache.TryGetValue((type, name), out var hit)) return hit;
        var all = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Where(m => m.Name == name).ToList();
        MethodInfo r = all.Count == 1 ? all[0] : Pick(type, name, all.Where(m => !m.IsStatic && m.GetParameters().Length == 1).ToList());
        lock (cache) cache[(type, name)] = r;
        return r;
    }

    static readonly Dictionary<(Type, string), MethodInfo> cache = new();

    static MethodInfo Pick(Type type, string name, List<MethodInfo> adds)
    {
        if (adds.Count == 0) return null;
        if (adds.Count == 1) return adds[0];
        Type elem = ElementType(type);
        return adds.FirstOrDefault(m => elem != null && m.GetParameters()[0].ParameterType == elem)
            ?? adds.FirstOrDefault(m => elem != null && m.GetParameters()[0].ParameterType.IsAssignableFrom(elem))
            ?? throw new AmbiguousMatchException($"{type}: no single Add for element {elem}");
    }

    /// <summary>XmlSerializer's rule (TypeScope.GetCollectionElementType): the element type is the Current type of the
    /// most-derived public GetEnumerator(). Net fields like NetNullableEnum hide the base enumerator with a string one.</summary>
    static Type ElementType(Type type)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var ge = t.GetMethod("GetEnumerator", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (ge == null) continue;
            var rt = ge.ReturnType;
            if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(IEnumerator<>)) return rt.GetGenericArguments()[0];
            var cur = rt.GetProperty("Current");
            if (cur != null) return cur.PropertyType;
        }
        return type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                   .Select(i => i.GetGenericArguments()[0]).FirstOrDefault();
    }

    /// <summary>Reflection mode reads a collection member into a temporary of the member's type, then copies the items
    /// into the existing instance for getter-only properties. Types with no parameterless constructor (OverlaidDictionary)
    /// can't be the temporary: use a List, which the copy step only enumerates.</summary>
    public static object CreateCollection(Type type)
    {
        var ci = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (ci != null) return ci.Invoke(Array.Empty<object>());
        return new List<object>();
    }

    /// <summary>Reflection mode assigns every field member with FieldInfo.SetValue. The emitted serializer fills a
    /// readonly collection field in place instead (GameLocation.objects is a readonly OverlaidDictionary tied to net state).
    /// The patched reader passes field members through here before binding SetValue.</summary>
    public static FieldInfo WrapField(FieldInfo fi)
    {
        if (fi.IsInitOnly && !fi.FieldType.IsValueType && typeof(System.Collections.IEnumerable).IsAssignableFrom(fi.FieldType) && fi.FieldType != typeof(string))
            return new InPlaceCollectionField(fi);
        return fi;
    }

    sealed class InPlaceCollectionField : FieldInfo
    {
        readonly FieldInfo f;
        MethodInfo add;
        public InPlaceCollectionField(FieldInfo f) { this.f = f; }
        public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, System.Globalization.CultureInfo culture)
        {
            var current = f.GetValue(obj);
            if (current == null || value == null || ReferenceEquals(current, value) || value is not System.Collections.IEnumerable items)
            {
                if (current == null) f.SetValue(obj, value, invokeAttr, binder, culture);
                return;
            }
            add ??= GetMethod(f.FieldType, "Add");
            var arg = new object[1];
            foreach (var item in items) { arg[0] = item; add.Invoke(current, arg); }
        }
        public override object GetValue(object obj) => f.GetValue(obj);
        public override FieldAttributes Attributes => f.Attributes;
        public override RuntimeFieldHandle FieldHandle => f.FieldHandle;
        public override Type FieldType => f.FieldType;
        public override Type DeclaringType => f.DeclaringType;
        public override string Name => f.Name;
        public override Type ReflectedType => f.ReflectedType;
        public override object[] GetCustomAttributes(bool inherit) => f.GetCustomAttributes(inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => f.GetCustomAttributes(attributeType, inherit);
        public override bool IsDefined(Type attributeType, bool inherit) => f.IsDefined(attributeType, inherit);
    }

    /// <summary>Flattened collection members ([XmlElement] on a collection) are read into a new collection and then
    /// assigned with the property setter. Getter-only ones (LibraryMuseum.museumPieces) are filled in place instead,
    /// as the emitted serializer does.</summary>
    public static PropertyInfo WrapProperty(PropertyInfo pi)
    {
        if (pi.SetMethod == null && pi.GetMethod != null && !pi.PropertyType.IsValueType && pi.PropertyType != typeof(string)
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(pi.PropertyType))
            return new InPlaceCollectionProperty(pi);
        return pi;
    }

    static void AddAll(object target, object items, Type declared, ref MethodInfo add)
    {
        add ??= GetMethod(declared, "Add");
        var arg = new object[1];
        foreach (var item in (System.Collections.IEnumerable)items) { arg[0] = item; add.Invoke(target, arg); }
    }

    sealed class InPlaceCollectionProperty : PropertyInfo
    {
        readonly PropertyInfo p;
        MethodInfo add;
        public InPlaceCollectionProperty(PropertyInfo p) { this.p = p; }
        public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, object[] index, System.Globalization.CultureInfo culture)
        {
            var current = p.GetValue(obj);
            if (current == null || value == null || ReferenceEquals(current, value)) return;   // no setter: nothing else can be done (the old serializers ignored it too)
            AddAll(current, value, p.PropertyType, ref add);
        }
        public override object GetValue(object obj, BindingFlags invokeAttr, Binder binder, object[] index, System.Globalization.CultureInfo culture) => p.GetValue(obj, invokeAttr, binder, index, culture);
        public override PropertyAttributes Attributes => p.Attributes;
        public override bool CanRead => p.CanRead;
        public override bool CanWrite => true;
        public override Type PropertyType => p.PropertyType;
        public override MethodInfo[] GetAccessors(bool nonPublic) => p.GetAccessors(nonPublic);
        public override MethodInfo GetGetMethod(bool nonPublic) => p.GetGetMethod(nonPublic);
        public override MethodInfo GetSetMethod(bool nonPublic) => p.GetSetMethod(nonPublic);
        public override ParameterInfo[] GetIndexParameters() => p.GetIndexParameters();
        public override Type DeclaringType => p.DeclaringType;
        public override string Name => p.Name;
        public override Type ReflectedType => p.ReflectedType;
        public override object[] GetCustomAttributes(bool inherit) => p.GetCustomAttributes(inherit);
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => p.GetCustomAttributes(attributeType, inherit);
        public override bool IsDefined(Type attributeType, bool inherit) => p.IsDefined(attributeType, inherit);
    }

    /// <summary>Under NativeAOT XmlSerializer.Mode is always ReflectionOnly, so Serialize/Deserialize/CanDeserialize send
    /// every serializer down the reflection path, even the build-time generated ones (which have no root type and crash
    /// importing a mapping). The patched XmlSerializer asks here instead: only plain XmlSerializer instances (the
    /// fallbacks) use reflection; generated typed serializers (subclasses) use their compiled reader/writer.</summary>
    public static bool ShouldUseReflection(System.Xml.Serialization.XmlSerializer serializer)
        => serializer.GetType() == typeof(System.Xml.Serialization.XmlSerializer);
}
