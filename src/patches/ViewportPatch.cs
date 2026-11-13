using Microsoft.Xna.Framework.Graphics;
using StardewValley;
namespace SdvAot;

/// <summary>Cutscenes, events and minigames draw into the 1280x960 render targets (the port's 0.5 zoom), but between
/// frames the device viewport is the 640x480 screen. Code that reads the viewport during an update without dividing
/// by the zoom lays things out for half the width and height: the same bug the PC game has at zooms other than 100%.
/// Audit 2026-11-09 (every GraphicsDevice.Viewport / window-size read in the game): the reads below are update-time
/// and zoom-unaware. Draw-time reads are fine (the bound target is the right size), and the minigames that use
/// localMultiplayerWindow / zoomLevel (CraneGame, Darts, FishingGame, HaleyCowPictures, MaruComet, MineCart,
/// PlaneFlyBy, RobotBlastoff fields, CalicoJack) already convert. The bus intro has its own patch (MinigamePatch).</summary>
public static class ViewportPatch
{
    /// <summary>SDV_VIEWPORT_FIX=0 restores the game's behaviour (for before/after comparisons).</summary>
    static readonly bool Enabled = System.Environment.GetEnvironmentVariable("SDV_VIEWPORT_FIX") != "0";

    /// <summary>Between frames: the size of the surface the game draws on. Inside a draw: the bound target's viewport.</summary>
    [ViewportFix("StardewValley.Minigames.GrandpaStory", ".ctor")]                  // farmer in the office pan
    [ViewportFix("StardewValley.Minigames.GrandpaStory", "tick")]                   // scene 4; scenes 5+ already divide
    [ViewportFix("StardewValley.Event", "InitializeEvent")]                         // farm events: camera on the front door
    [ViewportFix("StardewValley.Event", "addSpecificTemporarySprite")]              // Emily's dance lights, parrots, ...
    [ViewportFix("StardewValley.Events.QiPlaneEvent", ".ctor")]                     // Qi's plane: height
    [ViewportFix("StardewValley.Events.QiPlaneEvent", "tickUpdate")]                // boxes dropped over the whole width
    [ViewportFix("StardewValley.EventScript_GreenTea", ".ctor")]                    // centring of the 1920x1080 scene
    [ViewportFix("StardewValley.Minigames.RobotBlastoff", "tick")]                  // sparkles over the whole screen
    [ViewportFix("StardewValley.Locations.MermaidHouse", "UpdateWhenCurrentLocation")]  // show sprites' start points
    [ViewportFix("StardewValley.Locations.Woods", "resetLocalState")]               // floating baubles
    [ViewportFix("StardewValley.Locations.IslandForestLocation", "resetLocalState")]
    [ViewportFix("StardewValley.Menus.ConfirmationDialog", ".ctor")]                // text wrap width
    public static Viewport DrawViewport(GraphicsDevice device)
    {
        Viewport vp = device.Viewport;
        if (device.RenderTargetCount != 0 || !Enabled) return vp;
        var game = Game1.game1;
        if (game == null || !game.ShouldDrawOnBuffer() || game.screen == null || game.screen.IsDisposed) return vp;
        return new Viewport(0, 0, game.screen.Width, game.screen.Height);
    }
}
