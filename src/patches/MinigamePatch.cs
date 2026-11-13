using System;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Minigames;
namespace SdvAot;

/// <summary>Minigames draw into the world render target, which is twice the screen at the port's 0.5 zoom (1280x960),
/// but between frames the device viewport is the 640x480 screen. The bus intro sizes itself from that viewport in its
/// constructor (an update), so it laid out a 213x160 view inside a 320x240 one: road markings, roadside objects and
/// the sky stopped part way across, and the camera pan ended too low (no sky). Same bug as the PC game at zooms other
/// than 100%. Intro only: other code that reads the viewport during updates divides by the zoom itself.</summary>
public static class MinigamePatch
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "screenWidth")] static extern ref int ScreenWidth(Intro intro);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "screenHeight")] static extern ref int ScreenHeight(Intro intro);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "transformMatrix")] static extern ref Matrix TransformMatrix(Intro intro);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "currentState")] static extern ref int CurrentState(Intro intro);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "globalYPan")] static extern ref float GlobalYPan(Intro intro);

    /// <summary>Size of the surface minigames are drawn on: the world render target, or the viewport without one.</summary>
    static Point DrawSize()
    {
        var game = Game1.game1;
        if (game != null && game.ShouldDrawOnBuffer() && game.screen != null && !game.screen.IsDisposed)
            return new Point(game.screen.Width, game.screen.Height);
        var vp = Game1.graphics.GraphicsDevice.Viewport;
        return new Point(vp.Width, vp.Height);
    }

    /// <summary>The intro is laid out for a view 240 art pixels high (3x at 720p). Whole scale closest to that on the
    /// real drawing surface: 1280x960 gets 4x, i.e. 2 screen pixels per art pixel, a 320x240 view.</summary>
    [Replace("StardewValley.Minigames.Intro", "changeScreenSize")]
    public static void IntroChangeScreenSize(Intro intro)
    {
        var size = DrawSize();
        intro.pixelScale = Math.Max(1, (int)Math.Round(size.Y / 240f));
        TransformMatrix(intro) = Matrix.CreateScale(intro.pixelScale);
        ScreenWidth(intro) = size.X / intro.pixelScale;
        ScreenHeight(intro) = size.Y / intro.pixelScale;
        intro.createBeginningOfLevel();
    }

    /// <summary>The pan down to the road (state 2) ends where the game computes from the update-time viewport height
    /// (-270 px here). End it where the 720p original frames the scene: 60 art pixels below the top, the strip of sky
    /// above the mountains.</summary>
    [Prefix("StardewValley.Minigames.Intro", "tick")]
    public static bool IntroTick(Intro intro, GameTime time, ref bool result)
    {
        if (CurrentState(intro) == 2)
        {
            int end = -60 * intro.pixelScale;
            if (GlobalYPan(intro) <= end)
            {
                GlobalYPan(intro) = end;
                var m = Matrix.CreateScale(intro.pixelScale);
                m.Translation = new Vector3(0f, end, 0f);
                TransformMatrix(intro) = m;
                CurrentState(intro) = 3;
            }
        }
        return false;
    }
}
