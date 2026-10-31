# PaP Hideout Module

The Call of Duty Zombies **Pack-a-Punch** machine, in your SPT hideout. Do Mechanic's quest, build the machine, feed it a gun and roubles, and take it back **115 Infused**. It hits harder, fires faster and kicks less, with the Pack-a-Punch camo, fire sound, tracers and red muzzle smoke.

Version 2.1.0, for **SPT 4.1.6**. Made by **Kennerz**.

**Making a gun mod?** See **[API.md](API.md)** to make your weapon work with the machine.

## Requirements

| Mod | Version | Why |
|---|---|---|
| SPT | 4.1.x (made on 4.1.6) | |
| [Hideout Uncensored](https://sp-mod.com/mod/3167/hideout-uncensored) by **trappuss** ([GitHub](https://github.com/trappuss/SPT-Mods-Release)) | 1.3.0 or newer, client **and** server | Lets you draw weapons in the hideout, which the machine needs |

Optional, by Kennerz, with their own upgraded forms: [Ray Gun](https://github.com/Kennerz77/RayGunSPT) and [Thundergun](https://github.com/Kennerz77/ThunderGunSPT).

## Installing

1. Close the game and the SPT server.
2. Extract the zip into your SPT folder (the one with `EscapeFromTarkov.exe`). You should end up with:
   - `BepInEx\plugins\PaPHideoutModule\`
   - `SPT_Runtime\user\mods\PaPHideoutServer\`
3. Start the server, then the game.

**Updating:** extract the new zip over the old one. Don't delete the mod folders first: that wipes your progress.

**Uninstalling:** delete `BepInEx\plugins\PaPHideoutModule\` and `SPT_Runtime\user\mods\PaPHideoutServer\`. Your profile stays healthy.

## How it plays

1. **"115 Riddles" (Mechanic).** Your hideout's side room must be open first (the "Emergency Wall" upgrades). Mechanic then asks you to find **five glowing 115 rock fragments on Factory**. Hand them in and he mails them back.
   - The rocks can't be sold or thrown away, and they exist nowhere else in the game.
   - If you lose one, he sends a spare.
2. **Build the Pack-a-Punch.** It's under a tarp in the side room. It needs the five rocks and 115,000 roubles. Once it's built, walk up and pick **Turn on**. It needs the hideout's power.
3. **Pack-a-Punch a gun.** Draw a weapon, look at the machine and pick **Insert gun**. Take the gun back out within 6 seconds, or the machine keeps it.

To undo an upgrade, right-click the infused gun and pick **Strip 115 powers**.

### 115 Infused

| | |
|---|---|
| Damage | x1.17 per bullet |
| Fire rate | x1.12 |
| Recoil | 6% less, half the sideways kick |
| Tracers | every bullet, red-purple |
| Smoke | red muzzle flash, red barrel smoke and red suppressor gas |
| Look | Pack-a-Punch camo on the gun and its frame (not sights, mags or other attachments), glowing inventory cell |
| Sound | the BO3 Pack-a-Punch fire sound |
| Price | normal price plus 500,000 roubles |

### Two machines, two camos

In F12, under **Machine model**, pick **Default** (Black Ops III) or **Origins** (the stone machine from Origins, with its own sounds). Each machine gives its own camo:

- **Default:** the BO1 Pack-a-Punch camo. Glossy black, with red circuitry that shows in the light and in reflections.
- **Origins:** slowly drifting blue crystal on the metal, and metal plating on the furniture and attachments.

The camo covers the whole gun body, including handguards, stocks and tan or FDE parts. Rubber, foam and fabric parts keep their own look.

**Re-pack:** want the other camo? Switch machines in F12, hold the upgraded gun at the machine and pick **Re-insert gun**. It costs 5,000 roubles and the gun keeps its upgrade.

### Insurance

Insured 115 guns come back from insurance still upgraded, with the same camo. Your other insurance returns aren't affected.

### Extras

- **Enemies with 115 guns:** about 1 enemy in 200 carries a Pack-a-Punched gun. Bring it home and it stays infused.
- **Sally no Mustang (cheat, off by default):** a Pack-a-Punched M1911A1 fires 40mm grenades.
- **Something in the hideout:** once the machine is built, look and listen for three green rocks.
- **A rare jingle:** now and then the machine plays the unreleased full Pack-a-Punch jingle.

## Settings (F12)

F12 has:
- volume sliders for everything: the machine, both machines' sounds, and the Pack-a-Punch shot and its tail
- looks-only mode (no stat bonuses, much cheaper)
- enemy 115 guns
- Sally
- the machine model
- **Debug > Restart 115 Riddles**, if your quest got stuck

Server settings (costs, quest texts, rock spots) are in `SPT_Runtime\user\mods\PaPHideoutServer\config.json`.

## For mod authors

Any gun works with the machine as it is. Use the [PaP API](API.md) to give your gun its own upgraded name, stats, fire sound, glow colour, and camo rules for both machines. It can also run server-side code when your gun is upgraded or stripped. API.md has setup, examples and a full reference.

## Credits

- **Kennerz**: the mod.
- **trappuss**: [Hideout Uncensored](https://sp-mod.com/mod/3167/hideout-uncensored) ([GitHub](https://github.com/trappuss/SPT-Mods-Release)), required for weapons in the hideout. It isn't included, so install it separately.
- **Pack-a-Punch machines, camos, rocks, sounds and music:** from **Call of Duty: Black Ops, Black Ops II and Black Ops III Zombies** (Treyarch / Activision), exported with Greyhound and GrayWolf.

This is a free, non-commercial fan mod. All Call of Duty assets belong to their owners.

## Building from source

- **Client** (BepInEx 5, net472): in `src\`, run `dotnet build -c Release`.
- **Server** (SPT 4.1.6, net10.0): in `server\`, run `dotnet build -c Release`.
- **Asset bundle:** `build_bundle.ps1` builds it with Unity 2022.3.43f1. Unity Hub must be running for the licence.

## Changelog

- **2.1.0:**
  - **Re-pack:** re-insert an upgraded gun in the other machine for 5,000 roubles to swap its camo. This works on the Ray Gun and Thundergun too.
  - **Insurance:** insured 115 guns come back still upgraded.
  - **Default camo:** reworked to match BO1: glossy black with spaced-out red circuitry that catches the light. It now covers handguards, stocks and FDE parts.
  - **Origins camo:** the crystal moves slowly, like something suspended in jelly. Dials get metal plating.
  - **Smoke:** red barrel smoke and suppressor gas on upgraded guns.
  - **Fixes:**
    - The Origins machine's hum loops without a click.
    - Origins inventory pictures are no longer too bright.
    - Camo textures no longer mix up the first time you look at or draw a gun.
  - **For mod authors:** `InfusedChanged` is raised on a re-pack, and the API docs are clearer.
- **2.0.0:**
  - The Origins Pack-a-Punch machine (F12), with its own sounds and the Origins crystal camo.
  - Mechanic mails the rocks back again, and the machine needs them. They can't be sold or thrown away, and he sends a spare if one goes missing.
  - F12 restart for the quest.
  - The camo is darker and shiny, and only on the gun itself and its frame.
  - Volume sliders for the Pack-a-Punch shot and tail.
  - The rare unreleased jingle.
  - The "please wait" flag drops when the gun is ready.
  - 6 second pick-up timer and faster rollers.
  - No "go to module" prompt at the machine in first person.
  - For mod authors: new camo, glow and Origins API calls (see API.md).
- **1.0.1:** the 115 rocks are quest items only, and the machine needs "115 Riddles" finished.
- **1.0.0:** first release.
