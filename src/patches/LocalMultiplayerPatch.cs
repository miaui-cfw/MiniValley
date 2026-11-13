using System.Collections.Generic;
using System.Reflection;
using StardewValley;
namespace SdvAot;

/// <summary>Split-screen co-op swaps every [InstancedStatic] field per player. The game emits IL for that at startup
/// (Reflection.Emit), which NativeAOT can't do. Same behaviour with a plain object[] holder and FieldInfo access.</summary>
public sealed class StaticHolder
{
    public readonly object[] Values = new object[LocalMultiplayerPatch.Fields.Count];
}

public static class LocalMultiplayerPatch
{
    internal static List<FieldInfo> Fields;
    static List<object> defaults;

    [Replace("StardewValley.LocalMultiplayer", "GenerateDynamicMethodsForStatics")]
    public static void GenerateDynamicMethodsForStatics()
    {
        const BindingFlags F = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        Fields = (List<FieldInfo>)typeof(LocalMultiplayer).GetField("staticFields", F).GetValue(null);
        defaults = (List<object>)typeof(LocalMultiplayer).GetField("staticDefaults", F).GetValue(null);
        LocalMultiplayer.StaticVarHolderType = typeof(StaticHolder);
        LocalMultiplayer.StaticSetDefault = h => { var v = ((StaticHolder)h).Values; for (int i = 0; i < v.Length; i++) v[i] = defaults[i]; };
        LocalMultiplayer.StaticSave = h => { var v = ((StaticHolder)h).Values; for (int i = 0; i < v.Length; i++) v[i] = Fields[i].GetValue(null); };
        LocalMultiplayer.StaticLoad = h => { var v = ((StaticHolder)h).Values; for (int i = 0; i < v.Length; i++) Fields[i].SetValue(null, v[i]); };
    }
}

/// <summary>GameRunner.SetInstanceDefaults deep-clones every static default (Force.DeepCloner: expression trees and
/// MakeGenericMethod over value types, not AOT-safe). Its only use is starting a split-screen player.
/// Single-player port: the first instance already has the defaults, so only the save step is kept.</summary>
public static class GameRunnerPatch
{
    [Replace("StardewValley.GameRunner", "SetInstanceDefaults")]
    public static void SetInstanceDefaults(InstanceGame instance)
    {
        if (GameRunner.instance != null && GameRunner.instance.gameInstances.Count > 1)
            throw new System.NotSupportedException("split-screen co-op is not supported in this port");
        GameRunner.SaveInstance(instance);
    }
}
