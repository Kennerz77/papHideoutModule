# PaP API: make your gun mod work with the Pack-a-Punch

PaP Hideout Module (by Kennerz) has a small API so any weapon mod can be Pack-a-Punched properly: its own upgraded name, stats, sound and look. Any gun already works with the machine without it. Use the API when you want your gun to have its own upgraded form.

Every call here works with **PaP Hideout Module 2.0.0 or newer**. 2.1.0 adds re-packing (see [section 4](#4-re-packing-and-insurance)). The [Ray Gun](https://github.com/Kennerz77/RayGunSPT) and [Thundergun](https://github.com/Kennerz77/ThunderGunSPT) mods by Kennerz use everything below, so look at them for full examples.

- [1. Set up (client)](#1-set-up-client)
- [2. The basics](#2-the-basics)
- [3. Is it upgraded?](#3-is-it-upgraded)
- [4. Re-packing and insurance](#4-re-packing-and-insurance)
- [5. The camo on your gun](#5-the-camo-on-your-gun)
- [6. Server side (optional)](#6-server-side-optional)
- [Quick reference](#quick-reference)

## 1. Set up (client)

Reference `BepInEx\plugins\PaPHideoutModule\PaPHideoutModule.dll`, but don't ship it. Add a **soft** dependency so your mod still loads without PaP:

```csharp
[BepInDependency("com.kennerz.paphideoutmodule", BepInDependency.DependencyFlags.SoftDependency)]
```

Put every `PaPApi` call in **one class**. Only touch that class when PaP 2.0.0 or newer is installed. Otherwise your mod crashes for players without it:

```csharp
internal static class MyPaP
{
    public static readonly bool Installed =
        Chainloader.PluginInfos.TryGetValue("com.kennerz.paphideoutmodule", out PluginInfo pap)
        && pap.Metadata.Version >= new Version(2, 0, 0);

    public static void Setup() { if (Installed) MyPaPBridge.Setup(); }
}

internal static class MyPaPBridge   // the only class that names PaPApi
{
    public static void Setup() { /* the calls below */ }
}
```

Call the API from the main thread, once, at startup. Your plugin's `Awake` is fine.

## 2. The basics

```csharp
using PaPHideoutModule;

string myGun = "your weapon template id";

PaPApi.RegisterName(myGun, "Zeus Cannon");            // upgraded name instead of "(115 Infused)"
PaPApi.RegisterBonuses(myGun, PaPBonusSet.None);      // no stat changes, or:
PaPApi.RegisterBonuses(myGun, new PaPBonusSet { Damage = 1.5f, FireRate = 1.35f, Recoil = 0.1f, Sideways = 0.5f });
PaPApi.RegisterShotSound(myGun, myShotClip);          // your own upgraded fire sound (an AudioClip)
PaPApi.NoShotTail(myGun);                             // no Pack-a-Punch tail after each shot
PaPApi.SetGlowColor(myGun, Color.red);                // the inventory glow and cell tint when upgraded
```

When the player picks "Looks and sound only" in F12, `PaPApi.BonusesActive` is false. Every gun then loses its bonuses, including your registered ones.

## 3. Is it upgraded?

```csharp
bool upgraded = PaPApi.IsInfused(item);       // also true for the hideout's weapons-mode copy
bool origins  = PaPApi.IsOriginsGun(item);    // upgraded (or re-packed) in the Origins machine
PaPApi.WeaponUpgraded += weapon => { };       // it just came back out of the machine, newly upgraded
PaPApi.InfusedChanged += () => { };           // anything was upgraded, stripped, re-packed or moved
string realId = PaPApi.OriginalId(copy.Id);   // the profile item behind a weapons-mode copy
```

`IsInfused` turns true as soon as the server accepts the upgrade, while the gun is still going into the machine. To switch your gun's look, wait for `WeaponUpgraded`.

## 4. Re-packing and insurance

These are new in 2.1.0. You don't need to do anything for them, but your gun's look should follow them.

- **Re-pack:** a gun upgraded in one machine can go through the other for 5,000 roubles, to swap the default camo for the Origins one or back. The gun stays upgraded and `WeaponUpgraded` is **not** raised. `IsOriginsGun` flips when the gun comes back out, and `InfusedChanged` is raised.
- **Insurance:** SPT gives insured items new ids when it mails them back. PaP moves the upgrade (and the Origins mark) to the new id, so `IsInfused` and `IsOriginsGun` keep working on the returned gun.

If your gun switches parts or materials between its default and Origins looks, check `IsOriginsGun` again in your `InfusedChanged` handler:

```csharp
PaPApi.InfusedChanged += () =>
{
    foreach (var (model, item) in MyLiveModels())
        MyLook.Set(model, PaPApi.IsInfused(item), PaPApi.IsOriginsGun(item));
};
```

## 5. The camo on your gun

By default the camo goes on the gun itself and its frame, including handguards, stocks and tan or FDE parts. It skips:

- transparent and alpha-tested materials
- soft materials ("rubber", "foam", "fabric", "leather", "pad", "cloth", "strap" in their name)
- sights, magazines and other attachments

Steer it by **material name**. Any part of the name matches, and case doesn't matter:

```csharp
PaPApi.ExcludeFromCamo(myGun);                        // no camo at all: you handle the upgraded look

// Default camo: paint every material with "mygun" in its name, except these
PaPApi.KeepMaterialsPlain("mygun", "glass", "wires", "stock");

// Origins camo: blue crystal (true) or metal plating (false)
PaPApi.SetOriginsLook("body", true);
PaPApi.SetOriginsLook("barrel", false);
PaPApi.SetOriginsPartLook("knob", false);             // by renderer (object) name, for parts sharing a material
PaPApi.KeepOriginsPlain("dial");                      // no Origins camo on these
PaPApi.PaintCutoutOnOrigins("control_box");           // let an alpha-tested material take the Origins camo
PaPApi.SplitOnRed("plastic");                         // per texel: red becomes crystal, the rest plating

PaPApi.ApplyCamo(model, item);                        // repaint a model you built yourself
```

The words given to `KeepMaterialsPlain` only apply to your own gun's materials. They never stop the camo on other guns.

PaP logs every material it paints or skips to `BepInEx\LogOutput.log` (`[camo] painted: ...` / `[camo] left plain: ...`). Use the log to find the names for these calls.

## 6. Server side (optional)

Reference `SPT_Runtime\user\mods\PaPHideoutServer\PaPHideoutServer.dll`. You can change the gun itself when it's upgraded or stripped, for example to attach parts or top up ammo. Register a handler from your mod's `OnLoad`:

```csharp
using PaPHideoutServer;

PaPServerApi.Register(myGunTemplateId, new PaPWeaponHandler
{
    Name = "My mod",
    CanUpgrade = ctx => ctx.InSlot("mod_magazine") == null ? "Load a magazine first." : null, // null = allowed
    Upgraded = ctx => ctx.Attach(upgradedBarrelTpl, "mod_barrel"),
    Stripped = ctx => ctx.Attach(stockBarrelTpl, "mod_barrel"),
});
```

Change items through the context with `InSlot`, `Attach`, `Remove` or `SetStack`. If you edit an item yourself, call `MarkChanged(item)` afterwards. PaP sends the changed gun back to the client. A re-pack doesn't call these handlers, because the gun doesn't change.

To infuse a gun yourself (a quest reward, a trader item), inject `PaPService` and call `Infuse(sessionId, itemId)`. There's also `Uninfuse`, `IsInfused` and `GetInfused`.

## Quick reference

**Client: `PaPHideoutModule.PaPApi`**

| Member | What it does |
|---|---|
| `RegisterName(tpl, name, shortName)` | The upgraded name, instead of "(115 Infused)" |
| `RegisterBonuses(tpl, set)` / `BonusesFor(item)` | The gun's own stat bonuses (`PaPBonusSet.None` for none) / the bonuses a gun gets |
| `RegisterShotSound(tpl, shot, tail)` / `NoShotTail(tpl)` | Its own upgraded fire sound / no Pack-a-Punch tail |
| `SetGlowColor(tpl, color)` | Inventory glow and cell tint colour when upgraded |
| `ExcludeFromCamo(tpl)` | No PaP camo at all on this gun |
| `KeepMaterialsPlain(word, words...)` | Default camo: which of your materials stay plain |
| `SetOriginsLook(word, crystal)` / `SetOriginsPartLook(word, crystal)` | Origins: crystal or plating, by material / renderer name |
| `KeepOriginsPlain(word)`, `PaintCutoutOnOrigins(word)`, `SplitOnRed(word)` | Origins: skip, paint alpha-tested, split red from the rest |
| `ApplyCamo(model, item)` | Paint (or clear) the camo on a model you built |
| `IsInfused(item or id)`, `IsOriginsGun(item)`, `InfusedIds`, `OriginalId(id)` | Upgrade state |
| `WeaponUpgraded`, `InfusedChanged` | Events (main thread; a failing listener is logged, not thrown) |
| `BonusesActive`, `DamageMultiplier`, `FireRateMultiplier`, `RecoilReduction`, `HorizontalRecoilReduction`, `InfusedColor` | The player's settings and the standard values |
| `Strip(id, done)`, `Refresh()` | Remove a gun's upgrade / re-read the list from the server |
| `IsFragment(item)`, `DressFragment(model, item, live)` | The 115 rock items |

**Server: `PaPHideoutServer`**

| Member | What it does |
|---|---|
| `PaPServerApi.Register(tpl, handler)` | Your `CanUpgrade` / `Upgraded` / `Stripped` rules |
| `PaPService.Infuse` / `Uninfuse` / `IsInfused` / `GetInfused` | Change or read the upgrade list for a profile |

## Credit

If your mod uses this, a shout-out to **Kennerz / PaP Hideout Module** is appreciated.
