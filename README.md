# PaP Hideout Module

The Black Ops 3 Zombies **Pack-a-Punch** machine, in your SPT hideout. Find the 115 rock fragments on Factory for Mechanic, build the machine, feed it a gun and 500,000 roubles, and take it back **115 Infused**: harder hitting, faster, steadier, wearing the BO1 Pack-a-Punch camo, with the BO3 Pack-a-Punch fire sound and red-purple tracers.

Version 1.0.0, for **SPT 4.1.6**.

## Requirements

| Mod | Version | Why |
|---|---|---|
| SPT | 4.1.x (made on 4.1.6) | |
| [Hideout Uncensored](https://sp-mod.com/mod/3167/hideout-uncensored) by **trappuss** ([GitHub](https://github.com/trappuss/SPT-Mods-Release)) | 1.3.0 or newer, **both** its client and server parts | It lets you draw weapons in the hideout, which the machine needs |

Both parts of this mod declare the dependency, so SPT and BepInEx will tell you if it's missing.

## Installing

1. Close the game and the SPT server.
2. Extract the zip into your SPT folder (the one with `EscapeFromTarkov.exe`). You should end up with:
   - `BepInEx\plugins\PaPHideoutModule\` (`PaPHideoutModule.dll`, `pap_hideout.bundle`, `pap_area_icon.png`, `pap_accept.wav`, `pap_deny.wav`)
   - `SPT_Runtime\user\mods\PaPHideoutServer\` (`PaPHideoutServer.dll`, `quest_115_riddles.png`)
3. Start the server, then the game.

Your existing profile works. The quest is offered once your hideout's side room (the blocked-off room you open with the "Emergency Wall" upgrades) is fully open.

## How it plays

### 1. "115 Riddles" (Mechanic)

Once the side room is open, Mechanic asks you to look into some strange rock fragments he found on Factory.

- Find the **five 115-infused rock fragments** on Factory (PMC raids). They glow blue and hum as you get close.
- Fragments you extract with are yours; the next raid only has the ones you're missing. Die with them and you lose them (like any quest item), and they come back in your next Factory raid.
- Hand them to Mechanic for 50,000 roubles, 5,000 XP and the fragments back in the mail: they're the missing parts of the machine.

### 2. Build the Pack-a-Punch

The machine is in the side room under an old tarp. Its hideout area appears in the area bar once the room is open.

- **Needs:** the five fragments Mechanic sends back, and 115,000 roubles. **Builds in 10 seconds.**
- When it's done, Mechanic sends his regards.
- The machine needs the hideout's power. When it's built (and after every power cut) it sits dark, its lights flickering, until you walk up and pick **Turn on**: it plays its start-up sound and blinks to life. While it runs it hums, its lights surge now and then, and it plays the Pack-a-Punch jingle every few minutes.

### 3. Pack-a-Punch a gun

1. Draw a weapon (**J** with Hideout Uncensored, or the machine's "Draw a weapon" option).
2. Look at the machine and pick **Insert gun (500,000 RUB)**.
3. Your character gives the "All right" gesture and says "Good work" as the gun rides into the machine. It comes back out, upgraded, on the ding of the sting.
4. Pick **Take weapon** within 10 seconds. If you don't, the machine keeps it.

Right-click an infused gun and pick **Strip 115 powers** to remove the upgrade.

### What 115 Infused does

| | |
|---|---|
| Damage | x1.17 per bullet (penetration unchanged) |
| Fire rate | x1.12 |
| Recoil | 6% less, and half the sideways kick and drift |
| Tracers | every bullet, red-purple, tracer ammo or not |
| Muzzle flash | red |
| Look | BO1 Pack-a-Punch camo on the gun and everything on it (never the bullets), in your hands, on your back and in your inventory, with a purple glow on its inventory cell |
| Sound | the BO3 Pack-a-Punch fire sound on every shot |
| Name | "(115 Infused)" |
| Trader price | its normal price **plus 500,000 roubles**, in the trader's currency |

Attachments wear the camo only while they sit on an infused gun: take a scope off and it's plain again, put any part on and it's painted.

### Extras

- **Sally no Mustang (CHEAT):** BO1's Pack-a-Punched M1911 was Mustang & Sally, a pair of grenade pistols; Tarkov gets Sally. A Pack-a-Punched **M1911A1** still loads .45 ACP (any type, normal mags and capacity), but every shot leaves the barrel as a 40mm M381 HE grenade. It flies faster and flatter than a launcher's, and explodes on impact once armed (about 3 m out) with a bigger blast (about 9 m), so don't fire it at anything right next to you. While it fires grenades the gun is named "(SALLY)" instead of "(115 Infused)". Off by default: turn it on in F12. Never active in "Looks and sound only" mode.

- **Enemies with 115 guns:** very rarely (1 enemy in 200) a scav, PMC, raider, boss or goon spawns with a Pack-a-Punched gun, and hits harder for it (with Sally no Mustang on, an enemy's Pack-a-Punched M1911A1 fires grenades too). Kill them and bring the gun home, and it stays 115 Infused.
- **Something in the hideout:** once the Pack-a-Punch is built, three green 115 rocks are hidden around the hideout (from your next visit). Look closely, and listen. Investigate all three and see what happens. They're back after your next raid.

## Settings (F12)

Only the things you'd want to change:

- **Volume:** machine hum, working hum, jingle, sting, crunch, power-on, voice line, Pack-a-Punch shot, Factory 115 rock hum, hideout green rock hum, 115 rock pickup, 115 song.
- **Looks and sound only (no weapon bonuses):** keeps everything you see and hear, without the damage, fire rate and recoil bonuses. The machine then costs 5,000 roubles, and an infused gun sells for 5,000 more instead of 500,000.
- **Enemies can carry a 115 gun:** on by default.
- **Sally no Mustang (CHEAT):** off by default. Turns projectiles into grenades, like good old times (see below).

Server settings (build cost and time, the quest's texts and fragment spots) are in `SPT_Runtime\user\mods\PaPHideoutServer\config.json`, written on the first start. Restart the server after editing it.

## Known issues

None known. Found one? Report it on the mod's Forge page, with your `BepInEx\LogOutput.log` and the server log.

## Updating

Close the game and the server, then extract the new zip over your install and let it overwrite. Your progress stays: the quest, the fragments, the Pack-a-Punch area and your infused guns are kept in files the zip doesn't contain (`config.json`, `infused.json`, `handed_in.json`, `greeted.json` and the `profiles` folder in `SPT_Runtime\user\mods\PaPHideoutServer\`).

Don't delete `SPT_Runtime\user\mods\PaPHideoutServer\` before updating: that's an uninstall, and wipes the mod's progress.

## Uninstalling

Delete `BepInEx\plugins\PaPHideoutModule\` and `SPT_Runtime\user\mods\PaPHideoutServer\`. Your profile stays healthy:

- Infused guns go back to being ordinary guns (which guns are infused is kept in the mod's own folder, not in your profile).
- The mod keeps everything the game wouldn't know without it (the fragment items, the quest's progress, the Pack-a-Punch area) out of your profile file, in its own folder, and adds it back when the server loads. Without the mod nothing adds it back, so your profile is as if the mod was never installed. SPT removes the finished quest's entry by itself.

## For mod authors

### Client: `PaPHideoutModule.dll`

Reference the DLL and add `[BepInDependency("com.kennerz.paphideoutmodule", BepInDependency.DependencyFlags.SoftDependency)]` (or hard). Call these from the main thread.

```csharp
using PaPHideoutModule;

bool infused = PaPApi.IsInfused(item);           // also true for the hideout's weapons-mode copy of an infused gun
string realId = PaPApi.OriginalId(copy.Id);      // the profile id behind a weapons-mode copy
IReadOnlyCollection<string> ids = PaPApi.InfusedIds;
PaPApi.InfusedChanged += () => { };              // the infused list changed
bool bonuses = PaPApi.BonusesActive;             // false in "Looks and sound only" mode
float dmg = PaPApi.DamageMultiplier;             // also FireRateMultiplier, RecoilReduction, HorizontalRecoilReduction, InfusedColor
PaPApi.ApplyCamo(model, item);                   // paint (or un-paint) a weapon model by the rules above
PaPApi.Strip(item.Id, ok => { });                // remove the upgrade (free)
PaPApi.Refresh();                                // re-read the infused list after your server mod changed it

// Mod weapons that work with the machine (a Ray Gun, a Thundergun...)
PaPApi.WeaponUpgraded += weapon => { };          // it just came out of the machine: switch to its upgraded form
PaPApi.RegisterShotSound(templateId, shot, tail); // its own Pack-a-Punch fire sound instead of the one picked by weapon class
PaPApi.ExcludeFromCamo(templateId);              // it keeps its own upgraded look (no BO1 camo)
PaPApi.RegisterName(templateId, "Zeus Cannon");  // its own upgraded name instead of the "(115 Infused)" suffix
PaPApi.RegisterBonuses(templateId, PaPBonusSet.None); // its own stat bonuses (None, or new PaPBonusSet { Damage = 1.5f, ... })
PaPBonusSet b = PaPApi.BonusesFor(weapon);       // the bonuses a weapon gets
```

`PaPApi.IsInfused` turns true as soon as the server confirms an upgrade, while the gun is still going into the machine. If your weapon changes its look when upgraded, wait for `WeaponUpgraded` (the gun coming back out) before showing it, as the camo does.

To work with or without this mod installed, use a soft dependency and keep the `PaPApi` calls in a class you only touch when `BepInEx.Bootstrap.Chainloader.PluginInfos` contains `PaPApi.PluginGuid`.

### Server: `PaPHideoutServer.dll`

`PaPService` is a singleton: take it as a constructor parameter in your server mod.

```csharp
service.IsInfused(sessionId, itemId);
service.Infuse(sessionId, itemId);    // free, e.g. a quest reward or a trader item
service.Uninfuse(sessionId, itemId);
service.GetInfused(sessionId);
```

`GET /pap/infused` returns the calling profile's infused ids. The client picks up changes the next time it asks (entering the hideout) or when a client mod calls `PaPApi.Refresh()`.

#### Changing what the machine does to a weapon

Register a handler (from your mod's `OnLoad`, any load order) to refuse an upgrade, or to change the gun when it's upgraded or stripped: attach parts, top up ammo, swap a part for its upgraded version.

```csharp
using PaPHideoutServer;

PaPServerApi.Register(myWeaponTemplateId, new PaPWeaponHandler
{
    Name = "My mod",
    // optional: return a message to refuse (shown to the player, nothing is paid)
    CanUpgrade = ctx => ctx.InSlot("mod_magazine") == null ? "Load a magazine first." : null,
    Upgraded = ctx =>
    {
        Item barrel = ctx.Attach(upgradedBarrelTpl, "mod_barrel");   // replaces what's in the slot
        ctx.Attach(muzzleTpl, "mod_muzzle", barrel);                 // onto a part
        if (ctx.InSlot("mod_magazine")?.Id is { } mag) { /* ... */ }
    },
    Stripped = ctx => ctx.Attach(stockBarrelTpl, "mod_barrel"),
});

// Or for any weapon, deciding per gun (a mystery box tracking the guns it gave out, say)
PaPServerApi.Register(new PaPWeaponHandler { Matches = ctx => myBoxGunIds.Contains(ctx.Weapon.Id.ToString()), Upgraded = ... });
```

The context offers `Weapon`, `Pmc`, `SessionId`, `Cosmetic` (looks-only upgrade), `Items`, `WeaponItems()`, `InSlot()`, `Attach()`, `Remove()` and `SetStack()`. Change items through these, or call `MarkChanged(item)` after editing one yourself: PaP then sends the changed gun back to the client in its own reply, and rebuilds the hideout's weapons-mode copy in the player's hands to match. Without that, the client wouldn't know about the change until the next login, and the inventory would be out of step with the server.

## Credits

- Pack-a-Punch machine, camo, rocks, sounds and music: **Call of Duty: Black Ops / Black Ops III Zombies** (Treyarch / Activision), exported with Greyhound. This is a free, non-commercial fan mod; all Call of Duty assets belong to their owners.
- **Hideout Uncensored** by trappuss ([page](https://sp-mod.com/mod/3167/hideout-uncensored), [GitHub](https://github.com/trappuss/SPT-Mods-Release)): the hideout weapons this mod depends on. Required; not included, install it separately.
- Made by Kennerz.

## Building from source

Client (BepInEx 5, net472): `src\`, `dotnet build -c Release`. Server (SPT 4.1.6, net10.0): `server\`, `dotnet build -c Release`. The asset bundle is built with Unity 2022.3.43f1 from `UnityAssets\` (`build_bundle.ps1`); its editor scripts read Greyhound's `.semodel`/`.seanim` exports of the BO3 assets directly. The builds copy their output into the SPT install set in the project files.

## Changelog

- **1.0.0:** first release.
