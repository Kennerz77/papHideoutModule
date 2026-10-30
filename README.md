# PaP Hideout Module

The Call of Duty Zombies **Pack-a-Punch** machine, in your SPT hideout. Do Mechanic's quest, build the machine, feed it a gun and roubles, and take it back **115 Infused**: harder hitting, faster, steadier, with the Pack-a-Punch camo, fire sound and tracers.

Version 2.0.0, for **SPT 4.1.6**. Made by **Kennerz**.

**Making a gun mod?** See **[API.md](API.md)** to make your weapon work with the machine.

## Requirements

| Mod | Version | Why |
|---|---|---|
| SPT | 4.1.x | |
| [Hideout Uncensored](https://sp-mod.com/mod/3167/hideout-uncensored) by **trappuss** ([GitHub](https://github.com/trappuss/SPT-Mods-Release)) | 1.3.0 or newer, client **and** server | Lets you draw weapons in the hideout, which the machine needs |

## Installing

1. Close the game and the SPT server.
2. Extract the zip into your SPT folder (the one with `EscapeFromTarkov.exe`).
3. Start the server, then the game.

**Updating:** extract the new zip over the old one. Don't delete the mod folders first: that wipes your progress.

**Uninstalling:** delete `BepInEx\plugins\PaPHideoutModule\` and `SPT_Runtime\user\mods\PaPHideoutServer\`. Your profile stays healthy.

## How it plays

1. **"115 Riddles" (Mechanic).** Once your hideout's side room is open (the "Emergency Wall" upgrades), Mechanic asks you to find **five glowing 115 rock fragments on Factory**. Hand them in and he mails them back. They can't be sold or thrown away, and they exist nowhere else in the game. Lose one and he sends a spare.
2. **Build the Pack-a-Punch.** It's under a tarp in the side room. It needs the five rocks and 115,000 roubles. Once built, walk up and pick **Turn on** (it needs the hideout's power).
3. **Pack-a-Punch a gun.** Draw a weapon, look at the machine, pick **Insert gun**. Take it back out within 6 seconds, or the machine keeps it.

Right-click an infused gun and pick **Strip 115 powers** to undo it.

### 115 Infused

| | |
|---|---|
| Damage | x1.17 per bullet |
| Fire rate | x1.12 |
| Recoil | 6% less, half the sideways kick |
| Tracers | every bullet, red-purple |
| Look | Pack-a-Punch camo on the gun and its frame (not sights, mags or other attachments), glowing inventory cell |
| Sound | the BO3 Pack-a-Punch fire sound |
| Price | normal price plus 500,000 roubles |

### Two machines

In F12 > **Machine model** pick **Default** (Black Ops III) or **Origins** (the stone machine from Origins, with its own sounds). Guns upgraded in the Origins machine get the **Origins camo**: flowing blue crystal on the metal, metal plating on the furniture and attachments.

### Extras

- **Enemies with 115 guns:** about 1 enemy in 200 carries a Pack-a-Punched gun. Bring it home and it stays infused.
- **Sally no Mustang (cheat, off by default):** a Pack-a-Punched M1911A1 fires 40mm grenades.
- **Something in the hideout:** once the machine is built, look and listen for three green rocks.
- **A rare jingle:** now and then the machine plays the unreleased full Pack-a-Punch jingle.

## Settings (F12)

Volumes for everything (machine, both machines' sounds, the Pack-a-Punch shot and its tail), looks-only mode (no stat bonuses, much cheaper), enemy 115 guns, Sally, the machine model, and **Debug > Restart 115 Riddles** if your quest got stuck.

Server settings (costs, quest texts, rock spots) are in `SPT_Runtime\user\mods\PaPHideoutServer\config.json`.

## Credits

- **Kennerz**: the mod.
- **trappuss**: [Hideout Uncensored](https://sp-mod.com/mod/3167/hideout-uncensored), required for weapons in the hideout. Not included; install it separately.
- Pack-a-Punch machines, camos, rocks, sounds and music: **Call of Duty: Black Ops, Black Ops II and Black Ops III Zombies** (Treyarch / Activision), exported with Greyhound and GrayWolf. A free, non-commercial fan mod; all Call of Duty assets belong to their owners.

## Building from source

Client (BepInEx 5, net472): `src\`, `dotnet build -c Release`. Server (SPT 4.1.6, net10.0): `server\`, `dotnet build -c Release`. Asset bundle: Unity 2022.3.43f1, `build_bundle.ps1`.

## Changelog

- **2.0.0:** the Origins Pack-a-Punch machine (F12) with its own sounds and the Origins crystal camo; Mechanic mails the rocks back again (unsellable, can't be thrown away, a spare if one goes missing) and the machine needs them; F12 restart for the quest; the camo is now darker and shiny, and only on the gun itself and its frame; volume sliders for the Pack-a-Punch shot and tail; the rare unreleased jingle; the "please wait" flag drops when the gun is ready; 6 second pick-up timer; faster rollers; no "go to module" prompt at the machine in first person. For mod authors: new camo, glow and Origins API calls (see API.md).
- **1.0.1:** the 115 rocks are quest items only; the machine needs "115 Riddles" finished.
- **1.0.0:** first release.
