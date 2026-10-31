using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT.Communications;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.Animations.NewRecoil;
using EFT.Ballistics;
using EFT.Hideout;
using JsonType;
using EFT.InventoryLogic;
using EFT.Trading;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Utils;
using TMPro;
using UnityEngine;

namespace PaPHideoutModule
{
    /// <summary>
    /// Game hooks for 115 Infused weapons: name suffix, stats (fire rate, recoil), damage per bullet fired from them,
    /// BO1 camo on their models and the blue glow on their inventory cells.
    /// </summary>
    internal static class PaPPatches
    {
        public static void Apply(Harmony harmony)
        {
            Patch(harmony, AccessTools.Method(typeof(LocalizationExtensions), nameof(LocalizationExtensions.LocalizedName), new[] { typeof(Item) }), nameof(NamePostfix));
            Patch(harmony, AccessTools.Method(typeof(LocalizationExtensions), nameof(LocalizationExtensions.LocalizedShortName), new[] { typeof(Item) }), nameof(ShortNamePostfix));

            Patch(harmony, AccessTools.PropertyGetter(typeof(Weapon), nameof(Weapon.FireRate)), nameof(FireRatePostfix));
            Patch(harmony, AccessTools.PropertyGetter(typeof(Weapon), nameof(Weapon.SingleFireRate)), nameof(FireRatePostfix));
            Patch(harmony, AccessTools.PropertyGetter(typeof(Weapon), nameof(Weapon.RecoilDelta)), nameof(RecoilDeltaPostfix));

            // Sideways kick: the shot's recoil direction, and the random sideways drift after each shot
            Patch(harmony, AccessTools.Method(typeof(NewRecoilShotEffect), nameof(NewRecoilShotEffect.CalculateRecoilDirection)), nameof(RecoilDirectionPostfix));
            Patch(harmony, AccessTools.Method(typeof(NewRecoilShotEffect), nameof(NewRecoilShotEffect.RecalculateRecoilParamsOnChangeWeapon)), nameof(RecoilParamsPostfix));

            Patch(harmony, AccessTools.Method(typeof(BallisticsCalculator), nameof(BallisticsCalculator.CreateShot)), nameof(CreateShotPostfix));
            // Mustang & Sally: a Pack-a-Punched M1911A1 fires 40mm grenades, faster and with a wider blast and a bigger
            // explosion than the launcher's (only its own: launcher rounds stay vanilla)
            Prefix(harmony, AccessTools.Method(typeof(BallisticsCalculator), nameof(BallisticsCalculator.CreateShot)), nameof(CreateShotPrefix));
            Prefix(harmony, AccessTools.Method(typeof(Grenade), nameof(Grenade.Explosion)), nameof(GrenadeExplosionPrefix));
            Prefix(harmony, AccessTools.Method(typeof(ClientGameWorld), nameof(ClientGameWorld.ShotDelegate)), nameof(ShotDelegatePrefix));
            Patch(harmony, AccessTools.Method(typeof(ClientGameWorld), nameof(ClientGameWorld.ShotDelegate)), nameof(ShotDelegatePostfix));
            Prefix(harmony, AccessTools.Method(typeof(Systems.Effects.Effects), nameof(Systems.Effects.Effects.EmitGrenade)), nameof(EmitGrenadePrefix));

            // 115-coloured tracers and muzzle flash
            Prefix(harmony, AccessTools.Method(typeof(TracersLight), nameof(TracersLight.CreateTracer)), nameof(TracerPrefix));
            Prefix(harmony, AccessTools.Method(typeof(MuzzleManager), nameof(MuzzleManager.Shot)), nameof(MuzzleShotPrefix));
            // The gas puffing out of the muzzle or suppressor, and the smoke trailing off the barrel, in the 115 colour too
            Prefix(harmony, AccessTools.GetDeclaredMethods(typeof(MuzzleManager)).FirstOrDefault(m => m.Name.EndsWith("IFumeEmitter.Emit")), nameof(FumePrefix));
            Patch(harmony, AccessTools.Method(typeof(MuzzleSmoke), nameof(MuzzleSmoke.CreatePoint)), nameof(SmokePointPostfix));

            Patch(harmony, AccessTools.Method(typeof(IconsHash), nameof(IconsHash.GetItemHash)), nameof(IconHashPostfix));
            Patch(harmony, AccessTools.Method(typeof(ItemView), "NewItemView"), nameof(ItemViewPostfix));
            Patch(harmony, AccessTools.Method(typeof(GridItemView), nameof(GridItemView.UpdateInfo)), nameof(GridViewInfoPostfix));
            // Now and then an enemy spawns carrying a 115 gun (F12 toggle)
            Patch(harmony, AccessTools.Method(typeof(GameWorld), nameof(GameWorld.RegisterPlayer)), nameof(RegisterPlayerPostfix));
            // A 115 rock picked up off the ground: its BO3 pickup sound where it lay
            Prefix(harmony, AccessTools.Method(typeof(EFT.Interactive.LootItem), nameof(EFT.Interactive.LootItem.RemoveLootItem)), nameof(RockPickupPrefix));

            Patch(harmony, AccessTools.FirstMethod(typeof(ObjectsFactory), m =>
                m.Name == nameof(ObjectsFactory.CreateItemAsync)
                && m.ReturnType == typeof(Task<GameObject>)
                && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(Item)), nameof(CreateItemPostfix));
            Patch(harmony, AccessTools.Method(typeof(WeaponPrefab), nameof(WeaponPrefab.InitHotObjects)), nameof(InitHotObjectsPostfix));
            // Every other way the game builds a weapon model: sync builds, loot/preview models (the inspect window's
            // 3D preview) and the models on the body (slung on the back, in the holster)
            foreach (MethodInfo create in AccessTools.GetDeclaredMethods(typeof(ObjectsFactory)).Where(m =>
                         (m.Name == nameof(ObjectsFactory.CreateItem) || m.Name == nameof(ObjectsFactory.CreateCleanLootPrefab)
                          || m.Name == nameof(ObjectsFactory.CreateLootPrefab) || m.Name == nameof(ObjectsFactory.CreateItemUsablePrefab))
                         && m.ReturnType == typeof(GameObject)
                         && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(Item)))
                Patch(harmony, create, nameof(CreateItemSyncPostfix));
            Patch(harmony, AccessTools.Method(typeof(PlayerBody.SlotView), nameof(PlayerBody.SlotView.CreateAndParent)), nameof(BodySlotPostfix));
            // The gun in the hands of the character on the loading screen and in the menus
            Patch(harmony, AccessTools.Method(typeof(PlayerModelLoader), nameof(PlayerModelLoader.CreateWeapon)), nameof(MenuWeaponPostfix));
            // Those models are mostly built from clones with new ids (icons, the inspect preview, the guns on the
            // body, the character on the loading screen): map each clone back to its original
            Patch(harmony, AccessTools.Method(typeof(ItemExtensions), nameof(ItemExtensions.CloneItemInternal)).MakeGenericMethod(typeof(Item)), nameof(ClonePostfix));

            // Tells the plugin when the hideout player exists, instead of scanning the scene for it
            Patch(harmony, AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.Init)), nameof(HideoutOwnerInitPostfix));

            // The Pack-a-Punch hideout area: its template before the game reads them, its scene object before the
            // hideout sets its areas up
            Prefix(harmony, AccessTools.PropertyGetter(typeof(HideoutAreasInfo), nameof(HideoutAreasInfo.Templates)), nameof(AreaTemplatesPrefix));
            Prefix(harmony, AccessTools.Method(typeof(HideoutController), nameof(HideoutController.HideoutAwake)), nameof(HideoutAwakePrefix));
            // Walking up to the machine must not offer the area's "go to module" prompt: it clashes with the machine's
            // own menu (a gun inside the machine would be left behind). The bird's-eye view's area icon stays
            Prefix(harmony, AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.HideoutAreaInteraction)), nameof(AreaInteractionPrefix));
            // The construction requirements draw the fragments' pictures even before they're found: "?" like any
            // unknown item until the player has examined one
            Patch(harmony, AccessTools.Method(typeof(HideoutItemView), nameof(HideoutItemView.UpdateInfo)), nameof(RequirementViewPostfix));
            // Locked, the area panel shows the next level's text (the built one); show the locked text instead
            Patch(harmony, AccessTools.Method(typeof(DescriptionPanel), nameof(DescriptionPanel.SetInfo)), nameof(DescriptionPostfix));
            // Hovering the area outlines the machine like the game outlines its own areas' buildings
            Patch(harmony, AccessTools.Method(typeof(HideoutController), nameof(HideoutController.SetHighlight)), nameof(HighlightPostfix));

            // Grid cell captions and the inspect window title don't go through LocalizedName
            Patch(harmony, AccessTools.Method(typeof(ItemFactory), nameof(ItemFactory.BriefItemName)), nameof(BriefNamePostfix));
            Patch(harmony, AccessTools.FirstMethod(typeof(InfoWindow), m =>
                m.Name == nameof(InfoWindow.Show)
                && m.DeclaringType == typeof(InfoWindow)
                && m.GetParameters().Any(p => p.ParameterType == typeof(ItemContext) && p.Name == "itemContext")), nameof(InfoWindowPostfix));

            // Inventory icons are rendered from a model the icon creator spawns itself; camo it before the photo
            Prefix(harmony, AccessTools.Method(typeof(ItemIconCreator), nameof(ItemIconCreator.RenderModel)), nameof(RenderModelPrefix));
            // The inspect window's 3D preview restores the lights it found on the model when it closes; a 115 rock's glow
            // light may be gone by then, and the error left an empty window that wouldn't close
            Prefix(harmony, AccessTools.Method(typeof(EFT.UI.WeaponModding.WeaponPreview), nameof(EFT.UI.WeaponModding.WeaponPreview.RestorePreviousObjectLightMasks)), nameof(PreviewLightsPrefix));
            // ...and the 115 rocks get their own picture in place of the studio's (which lights them flat white)
            Patch(harmony, AccessTools.Method(typeof(ItemIconCreator), nameof(ItemIconCreator.RenderModel)), nameof(RenderModelPostfix));

            Patch(harmony, AccessTools.Method(typeof(Player), nameof(Player.OnMakingShot)), nameof(MakingShotPostfix));

            // 115 bonuses in the stats panel (inspect window, modding screen)
            Prefix(harmony, AccessTools.Method(typeof(ItemSpecificationPanel), nameof(ItemSpecificationPanel.Show)), nameof(SpecPanelPrefix));

            // Infused guns sell to traders for their worth plus what the machine cost (the server pays the price the
            // client offers)
            Patch(harmony, AccessTools.Method(typeof(Trader), nameof(Trader.GetUserItemPrice)), nameof(TraderPricePostfix));

            // Right-click "Strip 115 powers" on infused weapons
            Patch(harmony, AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.GetItemContextInteractions)), nameof(ContextMenuPostfix));

            // A part taken off a 115 gun (or put on one): its camo follows once the move has landed
            Patch(harmony, AccessTools.Method(typeof(ItemController), "RaiseAddEvent"), nameof(ItemMovedPostfix));
            Patch(harmony, AccessTools.Method(typeof(ItemController), "RaiseRemoveEvent"), nameof(ItemMovedPostfix));
        }

        // The cell for a part taken off a gun is built (and its picture asked for) while the part still sits on the
        // gun, so it came out in camo and nothing asked again; re-check once the move is done
        private static void ItemMovedPostfix() => PaPHideoutModulePlugin.MarkItemsMoved();

        /// <summary>What an infused gun adds to its trader price, in roubles: the machine's fee.</summary>
        // Looks-only Pack-a-Punch costs (and adds) far less
        private static int InfusedSellBonusRub => PaPHideoutModulePlugin.Bonuses ? 500_000 : 5_000;

        private static void TraderPricePostfix(Trader __instance, Item item, ref Trader.ItemPrice? __result)
        {
            try
            {
                // No price means this trader doesn't buy it; that stays so
                if (__result == null || item == null)
                    return;
                int infused = item.GetAllItems().Count(Infused);
                if (infused == 0)
                    return;
                Trader.ItemPrice price = __result.Value;
                double course = price.CurrencyId.HasValue && __instance.CurrencyCourses != null
                                && __instance.CurrencyCourses.TryGetValue(price.CurrencyId.Value.ToString(), out double c) && c > 0 ? c : 1.0;
                long amount = price.Amount + (long)Math.Floor(infused * InfusedSellBonusRub / course);
                __result = new Trader.ItemPrice(price.CurrencyId, (int)Math.Min(amount, int.MaxValue));
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP sell price failed: {e.Message}");
            }
        }

        private static void SpecPanelPrefix(ItemContext itemContext)
        {
            try
            {
                PaPStats.Sync(itemContext?.Item);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP stats failed: {e}");
            }
        }

        private const string StripKey = "STRIP 115 POWERS";

        private static void ContextMenuPostfix(object __result, ItemContext itemContext)
        {
            try
            {
                Item item = itemContext?.Item;
                if (item == null || Singleton<AbstractGame>.Instance?.InRaid == true)
                    return;
                if (!(Traverse.Create(__result).Field("_dynamicInteractions").GetValue() is IDictionary interactions))
                    return;

                if (Infused(item))
                    interactions[StripKey] = new DynamicContextInteraction(item.Id, StripKey, () => ConfirmStrip(item), StripIcon);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP context menu failed: {e}");
            }
        }

        private static Sprite StripIcon => EFTHardSettings.Instance.StaticIcons.WishlistSprites[EWishlistGroup.Other];

        private static void ConfirmStrip(Item item)
        {
            string name = item.Name.Localized(); // without the "(115 Infused)" suffix
            ItemUiContext.Instance.ShowMessageWindow($"Remove 115 powers from {name}?",
                () => Strip(item), () => { }, "Strip 115 powers", 0f, false, TextAlignmentOptions.Center);
        }

        private static void Strip(Item item)
        {
            string id = PaPRegistry.OriginalId(item.Id);
            string name = item.LocalizedName();
            int before = PaPCopies.Signature(id);
            PaPServer.Send(PaPServer.Strip, id, result =>
            {
                PaPRegistry.Refresh();
                PaPCopies.RefreshIfChanged(id, before);
                if (!PaPRegistry.IsInfused(id))
                    NotificationManager.DisplayMessageNotification($"{name}: the 115 fades.", ENotificationDurationType.Default, ENotificationIconType.Default, null);
                else
                    NotificationManager.DisplayWarningNotification($"Couldn't strip {name}: {result?.Error ?? "no response from server"}", ENotificationDurationType.Default);
            });
        }

        private static void BriefNamePostfix(Item item, ref string __result)
        {
            if (!Infused(item) || __result == null)
                return;
            if (UpgradedName(item, out var names))
                __result = names.shortName;
            else if (!__result.EndsWith(PaPHideoutModulePlugin.ShortNameSuffix))
                __result += " " + PaPHideoutModulePlugin.ShortNameSuffix;
        }

        private static void InfoWindowPostfix(InfoWindow __instance, ItemContext itemContext)
        {
            try
            {
                Item item = itemContext?.Item;
                if (!Infused(item))
                    return;

                // The title is filled from the plain localized name, or from the brief name that already carries the
                // short " 115"; find that label and give it the full suffix
                string plain = item.Name.Localized();
                string brief = plain + " " + PaPHideoutModulePlugin.ShortNameSuffix;
                string full = plain + " " + PaPHideoutModulePlugin.NameSuffix;
                // A weapon with its own upgraded name (PaPApi.RegisterName) shows that instead
                if (UpgradedName(item, out var names))
                {
                    brief = names.shortName;
                    full = names.name;
                }
                foreach (TextMeshProUGUI label in __instance.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    string text = label.text?.Trim();
                    if (text == plain || text == brief)
                        label.text = full;
                }
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP window name failed: {e}");
            }
        }

        // Every item, infused or not: a pooled model can still wear the camo from its last use (also after the last
        // 115 gun was stripped), and must be photographed plain
        private static void RenderModelPrefix(Item item, object[] __args)
        {
            if (item == null || !(__args[1] is Delegate factory))
                return;
            __args[1] = Delegate.CreateDelegate(factory.GetType(), new CamoIconFactory(factory, item), nameof(CamoIconFactory.Run));
        }

        /// <summary>
        /// Wraps the icon creator's sprite factory so the model gets the camo (or loses it) before rendering, and a
        /// 115 fragment is photographed as its rock instead of the watch it was cloned from.
        /// </summary>
        private class CamoIconFactory
        {
            private readonly Delegate _inner;
            private readonly Item _item;
            private readonly bool _camo;

            public CamoIconFactory(Delegate inner, Item item)
            {
                _inner = inner;
                _item = item;
                // Decided now, in the same call as the icon hash (GetItemIcon -> RenderModel), not when the photo is
                // taken frames later: a gun infused or released in between was photographed in camo and cached under
                // its plain hash, which every identical item shares (stock trader AUGs, empty STANAGs)
                _camo = CamoInHash(item);
            }

            public Task<Sprite> Run(GameObject model, PreviewPivot pivot)
            {
                if (PaPFragments.Is(_item) || PaPFragments.SharesModel(_item))
                    DressSafe(model, _item, false);
                else
                    ApplyCamoSafe(model, _item, _camo);
                return (Task<Sprite>)_inner.DynamicInvoke(model, pivot);
            }
        }

        /// <summary>Whether the icon hash marks this item as pictured in camo (see <see cref="IconHashPostfix"/>).</summary>
        private static bool CamoInHash(Item item)
        {
            try
            {
                return !PaPFragments.Is(item) && !PaPCamo.IsHeld(item) && PaPCamo.OnInfusedGun(item) && PaPAssets.CamoReady;
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"PaP icon camo check skipped: {e.Message}");
                return false;
            }
        }

        // The studio lights the rocks flat white whatever their material says: its photo only gives the size, and the
        // rock's own picture (rendered in Unity) goes in its place, on the finished result. Waits for the bundle if it's
        // still loading, so a white photo is never what gets cached
        private static void RenderModelPostfix(Item item, ref Task<IconCreatorBase<Item, ItemIcon>.RenderModelResult> __result)
        {
            if (PaPFragments.Is(item) && __result != null)
                __result = WithRockImage(__result, item);
        }

        private static readonly AccessTools.FieldRef<EFT.UI.WeaponModding.WeaponPreview, List<(Light light, int mask)>> PreviewLights =
            AccessTools.FieldRefAccess<EFT.UI.WeaponModding.WeaponPreview, List<(Light light, int mask)>>("_cachedLightMasks");

        private static void PreviewLightsPrefix(EFT.UI.WeaponModding.WeaponPreview __instance)
        {
            try
            {
                PreviewLights(__instance)?.RemoveAll(entry => entry.light == null);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP preview lights guard failed: {e.Message}");
            }
        }

        private static async Task<IconCreatorBase<Item, ItemIcon>.RenderModelResult> WithRockImage(Task<IconCreatorBase<Item, ItemIcon>.RenderModelResult> task, Item item)
        {
            var result = await task;
            if (result.sprite == null)
                return result;
            try
            {
                while (PaPAssets.Preloading)
                    await Task.Yield();
                Texture2D image = PaPFragments.IconImage(item);
                if (image != null)
                {
                    result.sprite = PaPAssets.SpriteLike(result.sprite, image);
                }
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP rock icon failed, keeping the studio's: {e.Message}");
            }
            return result;
        }

        private static void MakingShotPostfix(Player __instance, IWeapon weapon)
        {
            if (weapon is Weapon w && PaPRegistry.IsInfused(w.Id))
                PaPShotAudio.OnShot(__instance.IsYourPlayer, __instance.GetInstanceID(), __instance.Position, w);
        }

        internal static HideoutPlayerOwner CurrentOwner;

        // Every hideout entry (also back from the main menu, when the hideout itself stays loaded)
        private static void HideoutOwnerInitPostfix(HideoutPlayerOwner __instance)
        {
            CurrentOwner = __instance;
            PaPHideoutRocks.NewVisit();
        }

        private static bool AreaInteractionPrefix(HideoutArea area, bool inSight) =>
            !(inSight && area != null && area.AreaTemplate != null && area.AreaTemplate.Type == PaPArea.Type);

        private static void AreaTemplatesPrefix(HideoutAreasInfo __instance)
        {
            try
            {
                PaPArea.InjectTemplate(__instance);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP area template failed: {e}");
            }
        }

        private static void RequirementViewPostfix(HideoutItemView __instance)
        {
            try
            {
                Item item = __instance.Item;
                if (!PaPFragments.Is(item))
                    return;
                Profile profile = ClientAppUtils.GetMainApp()?.GetClientBackEndSession()?.Profile;
                if (profile != null && !profile.Examined(item))
                    __instance.Animator.SetExaminedState(false);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP requirement icon failed: {e.Message}");
            }
        }

        private static void DescriptionPostfix(DescriptionPanel __instance)
        {
            try
            {
                AreaData area = __instance.AreaData;
                if (area?.Template?.Type == PaPArea.Type && area.CurrentLevel < 1 && __instance._descriptionLabel != null)
                    __instance._descriptionLabel.text = $"hideout_area_{(int)PaPArea.Type}_stage_0_description".Localized();
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP area description failed: {e.Message}");
            }
        }

        // The area's own highlight object is an empty marker (where the construction dust puffs); outline the
        // machine instead
        private static void HighlightPostfix(AreaData data, bool value)
        {
            try
            {
                Transform machine = PaPHideoutModulePlugin.Machine;
                if (!value || machine == null || data?.Template?.Type != PaPArea.Type)
                    return;
                HighLightMesh highlighter = EFT.CameraControl.CameraManager.Instance?.MeshHighlighter;
                if (highlighter != null)
                    highlighter.Target = machine;
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP area highlight failed: {e.Message}");
            }
        }

        private static void HideoutAwakePrefix(HideoutController __instance)
        {
            try
            {
                PaPArea.InjectSceneArea(__instance, PaPHideoutModulePlugin.MachinePosition, PaPHideoutModulePlugin.MachineRotation);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP area scene object failed: {e}");
            }
        }

        // Each hook stands alone: one that no longer fits after a game update is logged and skipped,
        // instead of taking the machine and every other 115 effect down with it
        private static void Patch(Harmony harmony, MethodBase target, string postfix) => Hook(harmony, target, postfix, false);

        private static void Prefix(Harmony harmony, MethodBase target, string prefix) => Hook(harmony, target, prefix, true);

        private static void Hook(Harmony harmony, MethodBase target, string method, bool prefix)
        {
            try
            {
                if (target == null)
                    throw new MissingMethodException("target not found");
                var hook = new HarmonyMethod(typeof(PaPPatches), method);
                harmony.Patch(target, prefix: prefix ? hook : null, postfix: prefix ? null : hook);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP hook {method} failed and is disabled: {e.GetBaseException().Message}");
            }
        }

        // ---------- Recoil ----------

        private static readonly AccessTools.FieldRef<NewRecoilShotEffect, Player.FirearmController> RecoilFirearm =
            AccessTools.FieldRefAccess<NewRecoilShotEffect, Player.FirearmController>("_firearmController");

        private static bool InfusedInHands(NewRecoilShotEffect recoil)
        {
            Player.FirearmController firearm = RecoilFirearm(recoil);
            return firearm != null && Infused(firearm.Item);
        }

        private static float SidewaysLeft(NewRecoilShotEffect recoil) => 1f - PaPBonuses.For(RecoilFirearm(recoil)?.Item).Sideways;

        private static void RecoilDirectionPostfix(NewRecoilShotEffect __instance)
        {
            if (PaPHideoutModulePlugin.Bonuses && InfusedInHands(__instance))
                __instance.RecoilDirection.x *= SidewaysLeft(__instance);
        }

        private static void RecoilParamsPostfix(NewRecoilShotEffect __instance)
        {
            if (PaPHideoutModulePlugin.Bonuses && InfusedInHands(__instance) && __instance.HandRotationRecoil != null)
                __instance.HandRotationRecoil.AfterRecoilOffsetHorizontalRange *= SidewaysLeft(__instance);
        }

        // ---------- Tracers and muzzle flash ----------

        /// <summary>Every bullet from an infused weapon draws a tracer in the 115 tracer colour, tracer ammo or not.</summary>
        private static bool TracerPrefix(TracersLight __instance, Shot shot)
        {
            try
            {
                if (!PaPHideoutModulePlugin.Tracers || shot?.Weapon == null || !Infused(shot.Weapon))
                    return true;

                List<Vector3> path = shot.PositionHistory;
                if (path.Count < 2)
                    return false;
                Vector3 from = path[path.Count - 2];
                Vector3 to = path[path.Count - 1];
                float step = (to - from).sqrMagnitude;
                // Same "has it really left the barrel yet" check as the game's own
                if (step < 0.001f || (path.Count == 2 && step < 100f))
                    return false;

                __instance.Color = TracerColor;
                __instance.CreateShotTracer(shot, from, to);
                return false;
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP tracer failed: {e}");
                return true;
            }
        }

        private static Color _tracerColor;
        private static Color _tracerSetting = Color.clear;

        // As bright as the game's red tracers, in the configured hue
        private static Color TracerColor
        {
            get
            {
                Color setting = PaPHideoutModulePlugin.TracerColor;
                if (setting != _tracerSetting)
                {
                    _tracerSetting = setting;
                    Color red = TaxonomyColor.tracerRed.ToColor();
                    float brightness = Mathf.Max(red.r, red.g, red.b) / Mathf.Max(setting.r, setting.g, setting.b, 0.001f);
                    _tracerColor = setting * brightness;
                    _tracerColor.a = red.a;
                }
                return _tracerColor;
            }
        }

        private static void MuzzleShotPrefix(MuzzleManager __instance)
        {
            try
            {
                PaPMuzzle.BeforeShot(__instance);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP muzzle flash failed: {e}");
            }
        }

        private static void FumePrefix(MuzzleManager __instance, ref Color32 color)
        {
            try
            {
                PaPMuzzle.TintFume(__instance, ref color);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP muzzle gas failed: {e}");
            }
        }

        private static void SmokePointPostfix(MuzzleSmoke __instance)
        {
            try
            {
                PaPMuzzle.TintSmokePoint(__instance);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP barrel smoke failed: {e}");
            }
        }

        private static bool Infused(Item item) => item is Weapon && PaPRegistry.IsInfused(item.Id);

        // Upgraded names other mods gave their weapons (PaPApi.RegisterName): template -> (name, short name)
        internal static readonly Dictionary<string, (string name, string shortName)> UpgradedNames = new Dictionary<string, (string, string)>();

        private static bool UpgradedName(Item item, out (string name, string shortName) names)
        {
            names = default;
            return UpgradedNames.Count > 0 && UpgradedNames.TryGetValue(item.TemplateId.ToString(), out names);
        }

        private static void NamePostfix(Item item, ref string __result)
        {
            if (!Infused(item))
                return;
            if (UpgradedName(item, out var names))
                __result = names.name;
            else
                __result += " " + (FiresGrenades(item) ? PaPHideoutModulePlugin.SallyNameSuffix : PaPHideoutModulePlugin.NameSuffix);
        }

        private static void ShortNamePostfix(Item item, ref string __result)
        {
            if (!Infused(item))
                return;
            if (UpgradedName(item, out var names))
                __result = names.shortName;
            else
                __result += " " + (FiresGrenades(item) ? PaPHideoutModulePlugin.SallyShortNameSuffix : PaPHideoutModulePlugin.ShortNameSuffix);
        }

        private static void FireRatePostfix(Weapon __instance, ref int __result)
        {
            if (PaPHideoutModulePlugin.Bonuses && PaPRegistry.IsInfused(__instance.Id))
                __result = Mathf.RoundToInt(__result * PaPBonuses.For(__instance).FireRate);
        }

        // RecoilDelta is the summed % modifier (e.g. -0.25 = 25% less recoil), the same one attachments add to
        private static void RecoilDeltaPostfix(Weapon __instance, ref float __result)
        {
            if (PaPHideoutModulePlugin.Bonuses && PaPRegistry.IsInfused(__instance.Id))
                __result -= PaPBonuses.For(__instance).Recoil;
        }

        private const string M1911A1 = "5e81c3cbac2bb513793cdc75";
        private const string M381Grenade = "5ede474b0c226a66f5402622"; // 40x46mm M381 (HE)

        /// <summary>A Pack-a-Punched M1911A1 with Mustang &amp; Sally on (and weapon bonuses on): it fires grenades.</summary>
        private static bool FiresGrenades(Item weapon) =>
            weapon is Weapon && PaPHideoutModulePlugin.MustangAndSally.Value && PaPHideoutModulePlugin.Bonuses
            && weapon.TemplateId.ToString() == M1911A1 && PaPRegistry.IsInfused(weapon.Id);

        // The magazine and chamber keep their .45 ACP; only the shot is built from a grenade round, so it flies with
        // the grenade's speed, weight and drag (the arc) and the game explodes it on impact (ShotDelegate looks for
        // the shot ammo's ExplosiveAmmoComponent). A new round per shot: hits are told apart by the ammo's id
        private static void CreateShotPrefix(ref Ammo ammo, Item weapon, ref float speedFactor)
        {
            try
            {
                // Only the gun's own .45 rounds: the grenade's shrapnel is fired as shots "from" this gun too, and
                // turning those into grenades set off a chain of explosions (shrapnel and grenades aren't .45 ACP)
                if (!FiresGrenades(weapon) || ammo == null || ammo.Caliber != ((Weapon)weapon).AmmoCaliber)
                    return;
                if (Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), M381Grenade, null) is Ammo grenade)
                {
                    ammo = grenade;
                    // Faster out of the barrel: a flatter, longer arc than the launcher's
                    speedFactor *= PaPHideoutModulePlugin.SallySpeedFactor;
                }
            }
            catch (Exception e)
            {
                // The .45 goes out as a .45 then
                PaPHideoutModulePlugin.Log.LogError($"Mustang & Sally failed: {e.Message}");
            }
        }

        // The blast is the grenade template's, shared with real launcher rounds; for this pistol's it goes through a
        // wider copy instead (the explosion tells us which weapon fired it)
        private static void GrenadeExplosionPrefix(ref IExplosiveItem grenadeItem, Item originalWeaponItem)
        {
            if (grenadeItem != null && !(grenadeItem is SallyBlast) && FiresGrenades(originalWeaponItem))
                grenadeItem = new SallyBlast(grenadeItem);
        }

        /// <summary>A Mustang &amp; Sally grenade's blast: the M381's, reaching further.</summary>
        private sealed class SallyBlast : IExplosiveItem
        {
            private readonly IExplosiveItem _inner;

            public SallyBlast(IExplosiveItem inner) => _inner = inner;

            public Vector3 Blindness => _inner.Blindness;
            public Vector3 Contusion => _inner.Contusion;
            public Vector3 ArmorDistanceDistanceDamage => _inner.ArmorDistanceDistanceDamage;
            public float MinExplosionDistance => _inner.MinExplosionDistance * PaPHideoutModulePlugin.SallyBlastScale;
            public float MaxExplosionDistance => _inner.MaxExplosionDistance * PaPHideoutModulePlugin.SallyBlastScale;
            public int FragmentsCount => _inner.FragmentsCount;
            public float GetStrength => _inner.GetStrength;
            public bool IsDummy => _inner.IsDummy;
            public Ammo CreateFragment() => _inner.CreateFragment();
        }

        // The explosion graphic is picked by name while the game handles the grenade's hit; for the pistol's grenade
        // the hand grenade's bigger one is shown instead of the launcher's
        private static bool _sallyHit;

        private static void ShotDelegatePrefix(Shot shotResult)
        {
            try
            {
                _sallyHit = shotResult?.Ammo != null && shotResult.Ammo.TemplateId.ToString() == M381Grenade && FiresGrenades(shotResult.Weapon);
            }
            catch (Exception)
            {
                _sallyHit = false;
            }
        }

        private static void ShotDelegatePostfix() => _sallyHit = false;

        private static readonly FieldInfo EffectNames = AccessTools.Field(typeof(Systems.Effects.Effects), "_names");

        private static void EmitGrenadePrefix(Systems.Effects.Effects __instance, ref string ename)
        {
            if (!_sallyHit)
                return;
            try
            {
                if ((EffectNames?.GetValue(__instance) as IDictionary)?.Contains(PaPHideoutModulePlugin.SallyExplosionEffect) == true)
                    ename = PaPHideoutModulePlugin.SallyExplosionEffect;
            }
            catch (Exception)
            {
                // the launcher's explosion then
            }
        }

        // Only bullets fired from an infused weapon hit harder; penetration is unchanged
        private static void CreateShotPostfix(Shot __result, Item weapon)
        {
            if (PaPHideoutModulePlugin.Bonuses && __result != null && weapon != null && PaPRegistry.IsInfused(weapon.Id))
                __result.Damage *= PaPBonuses.For(weapon).Damage;
        }

        // Bumped to retire cached pictures: "PaP8" the washed-out rocks of older builds; "PaP9"/"PLN4" (1.1.0) guns and
        // parts photographed with camo stuck on (a part's own copy of a painted material, which 1.0.0 couldn't undo)
        private const int IconSalt = 0x50615043; // bumped: the camo rules changed, so every picture is redone
        private const int PlainPartSalt = 0x504C4E34; // "PLN4"

        // Icons are cached by hash; anything that changes the camo on any part of the item must change the hash, or
        // the inventory keeps showing the old picture (e.g. a camo'd scope moved onto another gun)
        private static void IconHashPostfix(Item item, ref int __result)
        {
            if (item == null)
                return;
            try
            {
                // Fragment icons used to be the watch they were cloned from (or washed out by the rock's light); a
                // new hash retires those. Not before the rock can load, or a rockless photo is cached as the rock one
                // Fragments: always their own hash (their picture waits for the bundle, so it's never a white photo)
                if (PaPFragments.Is(item))
                {
                    __result = __result * 31 + 1153 + IconSalt;
                    return;
                }
                // A gun still inside the machine is pictured plain until it comes back out, under its own hash so that
                // picture is never mistaken for the plain gun's or the camo one later
                if (PaPCamo.IsHeld(item))
                {
                    __result = __result * 31 + 0x48454C44; // "HELD"
                    return;
                }
                // An infused gun, or a part sitting on one, is pictured in camo; the same part elsewhere keeps the plain
                // hash, so moving it on or off a 115 gun switches its picture. The salt also retires icons the game
                // saved to its disk cache under older PaP versions. Before the camo texture is loaded the plain hash is
                // kept, so a picture taken then is never cached as the camo one
                if (PaPCamo.OnInfusedGun(item) && PaPAssets.CamoReady)
                    __result = __result * 31 + 115115 + IconSalt + (PaPCamo.OnOriginsGun(item) ? 0x0B15 : 0);
                // Parts, mags and the like off a gun: older builds could photograph them still wearing the camo of the
                // gun they came from and cache that as their plain picture (also on disk, and in trader screens); a
                // plain salt retires those once
                else if (item is Mod || item is CompoundItem)
                    __result = __result * 31 + PlainPartSalt;
            }
            catch (Exception e)
            {
                // A half-moved part must never break the game's icon lookup; the plain hash is fine for one frame
                PaPHideoutModulePlugin.Log.LogWarning($"PaP icon hash skipped: {e.Message}");
            }
        }

        // Item views the game has built, so a refresh doesn't have to scan the scene for them
        private static readonly HashSet<ItemView> ItemViews = new HashSet<ItemView>();

        internal static IEnumerable<ItemView> LiveItemViews()
        {
            ItemViews.RemoveWhere(v => v == null);
            return ItemViews.Where(v => v.isActiveAndEnabled).ToList();
        }

        /// <summary>One enemy in this many spawns with a 115 gun: rare enough to surprise.</summary>
        private const int EnemyPaPOdds = 200;

        private static readonly EquipmentSlot[] GunSlots = { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster };

        // Any AI (scav, PMC, raider, boss, follower, goon...) may roll it; the gun is infused for the raid, and stays
        // infused if the player kills them and brings it home
        private static void RegisterPlayerPostfix(IPlayer iPlayer)
        {
            try
            {
                if (!PaPHideoutModulePlugin.EnemyPaP.Value || !(iPlayer is Player player) || !player.IsAI
                    || UnityEngine.Random.Range(0, EnemyPaPOdds) != 0)
                    return;
                InventoryEquipment equipment = player.Profile?.Inventory?.Equipment;
                Weapon gun = GunSlots.Select(slot => equipment?.GetSlot(slot)?.ContainedItem).OfType<Weapon>().FirstOrDefault();
                if (gun == null)
                    return;
                PaPRegistry.MarkEnemyGun(gun.Id);
                PaPHideoutModulePlugin.Log.LogInfo($"{player.Profile.Info?.Nickname} ({player.Profile.Info?.Settings?.Role}) carries a 115 Infused {gun.Name.Localized()}.");
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP enemy gun roll failed: {e.Message}");
            }
        }

        // Same check as the game's own pickup sound: the item really left the ground (the loot is about to go)
        private static void RockPickupPrefix(EFT.Interactive.LootItem __instance, RemoveItemEventArgs args)
        {
            try
            {
                if (args == null || args.Status != CommandStatus.Succeed || !(args.From is OwnerItself) || args.Item?.Id != __instance.ItemId
                    || !PaPFragments.Is(args.Item))
                    return;
                AudioClip clip = PaPAssets.RockPickupClip;
                if (clip != null)
                    AudioSource.PlayClipAtPoint(clip, __instance.transform.position, PaPHideoutModulePlugin.RockPickupVolume.Value);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP rock pickup sound failed: {e.Message}");
            }
        }

        // Cells keep their view when a part comes off or goes on (the game only updates them): keep the glow in step
        private static void GridViewInfoPostfix(GridItemView __instance)
        {
            try
            {
                Item item = __instance.Item;
                PaPGlow.Attach(__instance, Infused(item) || PaPFragments.Is(item));
                if (PaPFragments.Is(item) || PaPCamo.OnInfusedGun(item))
                    PaPHideoutModulePlugin.TrackIconView(__instance);
            }
            catch (Exception)
            {
                // a half-built cell; the next update fixes it
            }
        }

        private static void ItemViewPostfix(ItemView __instance, Item item)
        {
            ItemViews.Add(__instance);
            // Remembered from the start, so stripping the gun later re-requests this cell's picture too (cells made
            // after the last refresh used to keep the camo photo)
            try
            {
                if (PaPFragments.Is(item) || PaPCamo.OnInfusedGun(item))
                    PaPHideoutModulePlugin.TrackIconView(__instance);
            }
            catch (Exception)
            {
                // a half-built cell; the next refresh picks it up
            }
            try
            {
                PaPGlow.Attach(__instance, Infused(item) || PaPFragments.Is(item));
            }
            catch (Exception e)
            {
                // never let the glow break the inventory
                PaPHideoutModulePlugin.Log.LogError($"PaP glow failed: {e}");
            }
        }

        /// <summary>
        /// Models of any item are checked against the camo rule once something is infused: pooled models of parts
        /// painted while on an infused gun come back out for that part alone (inspect preview, a mag's picture).
        /// </summary>
        private static bool Paintable(Item item) => item is Weapon || (item != null && !(item is Ammo) && PaPRegistry.AnyInfused);

        private static void CreateItemPostfix(ref Task<GameObject> __result, Item item)
        {
            if (PaPFragments.Is(item) || PaPFragments.SharesModel(item))
                __result = WithRock(__result, item);
            else if (Paintable(item))
                __result = WithCamo(__result, item);
        }

        private static async Task<GameObject> WithCamo(Task<GameObject> task, Item item)
        {
            GameObject go = await task;
            ApplyCamoSafe(go, item);
            return go;
        }

        private static async Task<GameObject> WithRock(Task<GameObject> task, Item item)
        {
            GameObject go = await task;
            DressSafe(go, item, true);
            return go;
        }

        private static void InitHotObjectsPostfix(WeaponPrefab __instance, Weapon weapon) => ApplyCamoSafe(__instance.gameObject, weapon);

        // Also the loot models: loose loot in raid and the inspect window's 3D preview
        private static void CreateItemSyncPostfix(GameObject __result, Item item)
        {
            if (__result == null)
                return;
            if (PaPFragments.Is(item) || PaPFragments.SharesModel(item))
                DressSafe(__result, item, true);
            else if (Paintable(item))
                ApplyCamoSafe(__result, item);
        }

        // Built through CreateItem (already painted), but the loading screen sets the character up afterwards and the
        // gun came out plain: paint it here, and again a moment later
        private static void MenuWeaponPostfix(PlayerModelLoader __instance, Item weapon)
        {
            try
            {
                WeaponPrefab prefab = __instance._weaponPrefab;
                if (!(weapon is Weapon) || prefab == null)
                    return;
                ApplyCamoSafe(prefab.gameObject, weapon);
                PaPCamoKeeper.Watch(prefab.gameObject, weapon);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP menu weapon camo failed: {e}");
            }
        }

        private static void ClonePostfix(Item originalItem, Item __result)
        {
            try
            {
                if (__result != null && originalItem != null && __result.Id != originalItem.Id && PaPRegistry.AnyInfused)
                    MapClone(originalItem, __result, 0);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP clone mapping failed: {e}");
            }
        }

        /// <summary>Walks original and clone slot by slot (clones may skip hidden contents, so not by item order).</summary>
        private static void MapClone(Item original, Item copy, int depth)
        {
            if (original == null || copy == null || original.TemplateId != copy.TemplateId || depth > 12)
                return;
            if (PaPRegistry.IsInfused(original.Id))
                PaPRegistry.AddCloneAlias(copy.Id, original.Id);
            if (!(original is ContainerCollection originalParts) || !(copy is ContainerCollection copyParts))
                return;
            foreach (Slot slot in originalParts.Containers.OfType<Slot>())
            {
                if (slot.ContainedItem == null)
                    continue;
                Slot copySlot = copyParts.Containers.OfType<Slot>().FirstOrDefault(s => s.ID == slot.ID);
                MapClone(slot.ContainedItem, copySlot?.ContainedItem, depth + 1);
            }
        }

        /// <summary>Rock for a fragment; for the real bronze watch, which shares its pooled models, the watch back.</summary>
        private static void DressSafe(GameObject go, Item item, bool live)
        {
            try
            {
                if (PaPFragments.Is(item))
                    PaPFragments.Dress(go, item, live);
                else
                    PaPFragments.Undress(go);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP fragment model failed: {e}");
            }
        }

        private static readonly AccessTools.FieldRef<PlayerBody.SlotView, Item> SlotItem =
            AccessTools.FieldRefAccess<PlayerBody.SlotView, Item>("_item");

        // A weapon going onto the sling or into the holster; its model can be one the hands used a moment ago
        private static void BodySlotPostfix(PlayerBody.SlotView __instance, GameObject model)
        {
            Item item = SlotItem(__instance);
            if (item is Weapon && model != null)
                ApplyCamoSafe(model, item);
        }

        private static void ApplyCamoSafe(GameObject go, Item item, bool? force = null)
        {
            try
            {
                PaPCamo.Apply(go, item, force);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogError($"PaP camo failed: {e}");
            }
        }
    }
}
