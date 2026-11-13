using System;
using System.Globalization;
using StardewValley;
namespace SdvAot;

/// <summary>640x480 screen: the game lays out for 1280x720+ (title menu hard-codes 2x/3x art). A fixed UI scale and
/// world zoom of 0.5 make the game lay out for 1280x960 and draw at half size, which keeps whole pixels:
/// 2x title art -> 1 px, 4x menu/world art -> 2 px. SDV_UI_SCALE / SDV_ZOOM override.</summary>
public static class ScreenPatch
{
    static readonly float ui = Read("SDV_UI_SCALE", 0.5f);
    static readonly float zoom = Read("SDV_ZOOM", 0.5f);

    static float Read(string name, float fallback)
        => float.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;

    [Replace("StardewValley.Options", "get_desiredUIScale")]
    public static float DesiredUIScale(Options options) => ui;

    [Replace("StardewValley.Options", "get_desiredBaseZoomLevel")]
    public static float DesiredBaseZoomLevel(Options options) => zoom;
}
