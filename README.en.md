# Mouse Shortcuts

[Subscribe on Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3831369892)

[简体中文](README.md) | [繁體中文](README.zh-TW.md) | English

Organize items, serve customers, and manage windows using just your mouse. Trade like a real boss.

## Items

- Use the scroll wheel to move items between your main inventory, the trading counter, and the display window.
- When you open a container, scroll actions prioritize that container.
- Middle-click to pin or unpin an item.
- Box-select multiple items to move them together with the scroll wheel, or pin and unpin the group with a middle-click.
- Automatically try rotating a dragged item when it won't fit.

## Customers and shop operations

- Scroll down to advance dialogue quickly; right-click to confirm a deal.
- Scroll up to open shop, dismiss a customer and call the next one. With no customer at the counter, scroll up or down to call the next one, or close up normally once the queue is empty.
- Keep scrolling to skip customer arrival, departure, and shutter animations.

## Windows

Right-click a blank area with no existing interaction to close the window.

Bindings, item destinations, and feature toggles can be adjusted in the mod settings.

This mod is still new and built around the controls I'm used to. There may still be bugs, missing features, or awkward controls. Feel free to point them out in the comments.

---

## Installation and settings

Download a version archive from [Releases](https://github.com/CH4ACKO3/MouseShortcuts/releases), extract its `RightClickClose` folder into the game's `Mods` directory, then restart the game and enable it in the mod menu. If you already have a local copy, overwrite its folder.

Select Mouse Shortcuts in the mod menu and click the gear icon to adjust its settings. Apply your changes for them to take effect. See the [usage guide (Simplified Chinese)](使用说明.txt) for the full default item routes.

## Development and releases

Requires the .NET 8 SDK or newer, PowerShell 7, and an installed copy of the game:

```powershell
./Tools/Build.ps1 -GameDir 'D:/App/Steam/steamapps/common/Probably Stolen'
```

Output goes to `artifacts/package`. CI build references come from game version 1.00-REV6 (Steam build 26337366). For local development, build against your own game installation.

Commits to `main` trigger a build. Tags in the form `v1.6.5` create a GitHub Release and, once Workshop publishing is enabled, update the same Workshop item. See the [publishing guide (Simplified Chinese)](docs/Publishing.md) for initial item creation and automated publishing setup.
