# Just_Be_Better

A Risk of Rain 2 mod that bundles a bunch of quality of life changes into one. Every part can be turned on or off on its own, either in the config file or in game with Risk Of Options, so it won't clash with another mod that does the same job.

## Features

- **Item Pickers**  
Adds search, favourites and item counts to the Command menu, Scrapper, Potentials and crafting menus. Big lists fit the window, items can be sorted by count, name or tier, and hovering an item shows its full description and stats. Moving closes the picker if you want it to.

- **Item Picker Safety**  
Pauses the world while you pick an item in single player, and protects you while picking in multiplayer, with a short grace period after. Escape closes the picker.

- **Item Info**  
Full descriptions and stat values on item tooltips, pickup cards and pings, showing what one more stack would give you. Values include your luck, with a "From Luck" line showing how much of it comes from luck. Covers the game's items, Aetherium's, and other mods' items where their stats can be read. Ally cards show what each drone or summon does.

- **Skill Info**  
Skill tooltips show the cooldown, proc coefficient, notes and the proc chances of your items.

- **Cooldown Timers**  
Skill and equipment icons show their countdown whenever a charge is refilling, not only at zero charges.

- **Buff Info**  
Timers on timed buffs and descriptions when you hover a buff.

- **Stats Display**  
A text panel under the objectives with your stats, DPS, combos, kills and portals, from a template you can edit.

- **Scoreboard Counters**  
Item counts by tier on the scoreboard, with temporary items counted apart.

- **Immune Health Bar**  
Shows IMMUNE on your health bar, and on the boss bar, while nothing can hurt you.

- **Chest Tweaks**  
Outlines chests, shops, drones and other interactables in one colour, and fades or hides used chests, barrels and shops.

- **Toggle Fire**  
A quick click keeps your primary firing until you click again, a long press works as a normal hold. Off by default.

- **Pickup Notifications**  
Turns off pickup cards and sets how long the game's other cards stay up.

- **Hidden Items and Buffs**  
Shows hidden items in inventories and hidden buffs on buff bars.

- **Inventory Sort**  
Sorts the inventory bar, scoreboard and enemy item panel by scrap, tier and stack size.

- **Lobby**  
A survivor grid with search, favourites and pages for big modded rosters, wrapping loadout and rule rows, and artifact votes kept across restarts.

- **Saves**  
Lets the host save a run, autosaves at every stage, and continues it later from the lobby. Retry from the last stage on death.

- **Cursor**  
Lets the mouse leave the game window in menus and while loading.

- **Loading**  
Speeds up startup with a faster effect scan and a cache of mods' slow asset bundles, and skips the logo screens and intro.

- **Equipment Sounds**  
Volume sliders for the equipment activation chime, equipment sounds and War Horn.

**Verbose Logging** in the General settings writes what each part is doing to the log. Leave it off unless you're testing or reporting a bug.

## Installing

Put `Just_Be_Better.dll` in `BepInEx\plugins\` and `Just_Be_Better.Patcher.dll` in `BepInEx\patchers\`. The patcher is only used by the Loading part, for the bundle cache and the startup timing.

| Dependency          | Needed for                  |
|---------------------|-----------------------------|
| **BepInEx**         | Everything                  |
| **HookGenPatcher**  | Everything (`MMHOOK_RoR2`)  |
| **Risk Of Options** | In game settings (optional) |

## Building

Needs the .NET SDK. Set `GameManaged` (the game's `Risk of Rain 2_Data\Managed` folder) and `ProfileDir` (your r2modman profile) in the `.csproj` files, then:

```
dotnet build -c Release
dotnet build patcher -c Release
```

The first builds the mod and the second the patcher, and both copy into that profile. Tests run with `dotnet run --project tests/ItemStats.Tests`.

## Credits and License

Item stat formulas, skill proc coefficients and buff descriptions are ported from [LookingGlass](https://github.com/Wet-Boys/LookingGlass) by DropPod, which is LGPL v3. This mod is LGPL v3 as well, see [LICENSE](LICENSE) and [COPYING](COPYING).

By Luci (LuciCMD).
