using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
namespace SdvAot;

/// <summary>RG35XX buttons follow the Nintendo labels, so the pad config maps them by label: physical A (right) is the
/// game's Buttons.A (check/confirm), physical B (bottom) is Buttons.B (cancel/menu), Y (left) is Buttons.X (use tool),
/// X (top) is Buttons.Y (crafting) - the Switch release's layout. These patches make the rest of the game agree.</summary>
public static class ControlsPatch
{
    /// <summary>The Switch ControllerMaps sheet (shipped in the pak) stacks four 288 px panels: Xbox, PlayStation,
    /// Joy-Con, PlayStation (Japan). The PC game always reads the Xbox panel; read the Joy-Con one, whose prompts
    /// show A on the right and B at the bottom like the console. Falls back to the PC sheet if that is what's loaded.</summary>
    [Replace("StardewValley.Utility", "controllerMapSourceRect")]
    public static Rectangle ControllerMapSourceRect(Rectangle xboxSourceRect)
    {
        var sheet = Game1.controllerMaps;
        if (sheet == null || sheet.Height < 4 * 288) return xboxSourceRect;
        return new Rectangle(xboxSourceRect.X, xboxSourceRect.Y + 2 * 288, xboxSourceRect.Width, xboxSourceRect.Height);
    }

    /// <summary>No accidental exits: B on the root title screen (nothing to go back to) moves the cursor to Exit
    /// instead of doing nothing; quitting still needs A on Exit. Submenus keep the game's own B = back.</summary>
    [Prefix("StardewValley.Menus.TitleMenu", "backButtonPressed")]
    public static bool TitleBack(TitleMenu menu)
    {
        if (TitleMenu.subMenu != null || !Game1.options.SnappyMenus || !menu.titleInPosition || menu.isTransitioningButtons) return false;
        if (menu.getComponentWithID(TitleMenu.region_exitButton) == null) return false;
        if (menu.currentlySnappedComponent?.myID != TitleMenu.region_exitButton) Game1.playSound("shiny4");
        menu.setCurrentlySnappedComponentTo(TitleMenu.region_exitButton);
        menu.snapCursorToCurrentSnappedComponent();
        return true;
    }

    /// <summary>End-of-day summary: the game takes B (or Start) as OK, which advances the day and saves. B must not
    /// confirm: on the summary page it only puts the cursor on OK (A confirms; Start keeps the game's shortcut).
    /// B on a category page still goes back to the summary.</summary>
    [Prefix("StardewValley.Menus.ShippingMenu", "receiveGamePadButton")]
    public static bool ShippingButton(ShippingMenu menu, Microsoft.Xna.Framework.Input.Buttons b)
    {
        if (b != Microsoft.Xna.Framework.Input.Buttons.B || menu.currentPage != -1 || !menu.CanReceiveInput()) return false;
        SnapTo(menu, 101);
        return true;
    }

    static void SnapTo(IClickableMenu menu, int id)
    {
        if (!Game1.options.SnappyMenus || menu.getComponentWithID(id) == null) return;
        menu.setCurrentlySnappedComponentTo(id);
        menu.snapCursorToCurrentSnappedComponent();
    }
}
