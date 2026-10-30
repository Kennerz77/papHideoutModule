# PaP API: make your gun mod work with the Pack-a-Punch

PaP Hideout Module (by Kennerz) has a small API so any weapon mod can be Pack-a-Punched properly: its own upgraded name, stats, sound and look. Any gun already works with the machine without it. Use the API when you want your gun to have its own upgraded form.

Needs **PaP Hideout Module 2.0.0 or newer**. The Ray Gun and Thundergun mods by Kennerz use everything below, so look at them for full examples.

## 1. Set up (client)

Reference `BepInEx\plugins\PaPHideoutModule\PaPHideoutModule.dll` (don't ship it) and add a **soft** dependency, so your mod still loads without PaP:

```csharp
[BepInDependency("com.kennerz.paphideoutmodule", BepInDependency.DependencyFlags.SoftDependency)]
```

Keep every `PaPApi` call in **one class** that you only touch when PaP 2.0.0+ is installed. Otherwise your mod crashes for players without it:

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

Call the API from the main thread, once, at startup (your plugin's `Awake` is fine).

## 2. The basics

```csharp
using PaPHideoutModule;

string myGun = "your weapon template id";

PaPApi.RegisterName(myGun, "Zeus Cannon");            // upgraded name instead of "(115 Infused)"
PaPApi.RegisterBonuses(myGun, PaPBonusSet.None);      // no stat changes, or:
PaPApi.RegisterBonuses(myGun, new PaPBonusSet { Damage = 1.5f, FireRate = 1.35f, Recoil = 0.1f, Sideways = 0.5f });
PaPApi.RegisterShotSound(myGun, myShotClip);          // your own upgraded fire sound (an AudioClip)
PaPApi.NoShotTail(myGun);                             // no Pack-a-Punch tail after each shot
```

## 3. Is it upgraded?

```csharp
bool upgraded = PaPApi.IsInfused(item);       // also true for the hideout's weapons-mode copy
bool origins  = PaPApi.IsOriginsGun(item);    // upgraded in the Origins machine
PaPApi.WeaponUpgraded += weapon => { };       // it just came back out of the machine
PaPApi.InfusedChanged += () => { };           // anything was upgraded or stripped
string realId = PaPApi.OriginalId(copy.Id);   // the profile item behind a weapons-mode copy
```

`IsInfused` turns true when the server accepts the upgrade, while the gun is still going into the machine. To switch your gun's look, wait for `WeaponUpgraded`.

## 4. The camo on your gun

By default the camo goes on the gun's metal parts and frame. It skips transparent materials, materials with "plastic", "polymer", "rubber" and similar in their name, and attachments. Steer it by **material name** (any part of the name, not case sensitive):

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

PaP writes a line for every material it paints or skips to `BepInEx\LogOutput.log` (`[camo] painted: ...` / `[camo] left plain: ...`). Use it to find the names to put in these calls.

## 5. Server side (optional)

Reference `SPT_Runtime\user\mods\PaPHideoutServer\PaPHideoutServer.dll`. To change the gun itself when it's upgraded or stripped (attach parts, top up ammo), register a handler from your mod's `OnLoad`:

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

Change items through the context (`InSlot`, `Attach`, `Remove`, `SetStack`, or `MarkChanged(item)` after editing one yourself). PaP sends the changed gun back to the client.

To infuse a gun yourself (a quest reward, a trader item), inject `PaPService` and call `Infuse(sessionId, itemId)`. There's also `Uninfuse`, `IsInfused` and `GetInfused`.

## Credit

If your mod uses this, a shout-out to **Kennerz / PaP Hideout Module** is appreciated.
