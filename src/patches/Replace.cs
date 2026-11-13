using System;
namespace SdvAot;
/// <summary>The patcher replaces the body of <c>type.method</c> with a call to the marked static method.
/// For an instance target, the replacement takes the instance as its first parameter.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ReplaceAttribute : Attribute
{
    public ReplaceAttribute(string type, string method) { }
}

/// <summary>The patcher inserts a call to the marked static bool method at the start of <c>type.method</c>; when it
/// returns true the original body is skipped. For an instance target, the instance is the first parameter. For a
/// target that returns a value, the prefix takes a trailing <c>ref</c> result parameter, returned when it skips.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class PrefixAttribute : Attribute
{
    public PrefixAttribute(string type, string method) { }
}

/// <summary>In every overload of <c>type.method</c> (".ctor" for constructors), calls to GraphicsDevice.get_Viewport
/// become calls to the marked static method (GraphicsDevice -> Viewport). Calls followed within a few instructions by
/// a read of Options.zoomLevel or Options.uiScale are left alone: that code already converts the window size.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ViewportFixAttribute : Attribute
{
    public ViewportFixAttribute(string type, string method) { }
}
