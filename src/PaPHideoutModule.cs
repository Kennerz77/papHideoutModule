using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Hideout;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace PaPHideoutModule
{
    /// <summary>
    /// BO3 Pack-a-Punch machine in the Hideout, as its own hideout area. Until the area is built the machine sits
    /// dead under a tarp; once built it runs while the hideout has power. Look at it with a weapon in your hands and
    /// press the interact key: the server takes the fee, the BO3 use sequence plays, and the weapon becomes
    /// "115 Infused". Weapons in the hideout come from Hideout Uncensored.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.SPT.core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(PaPUncensored.Guid, "1.3.0")] // hard: weapons in the hideout come from Hideout Uncensored
    public class PaPHideoutModulePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.kennerz.paphideoutmodule";
        public const string PluginName = "Kennerz-PaPHideoutModule"; // Forge rule: "Username-ModName"
        public const string PluginVersion = "1.0.0";

        private const string InstanceName = "PaP_Machine (PaPHideoutModule)";
        // Looks and sound only (F12): no weapon bonuses, so it costs (and adds to a gun's price) far less
        private static int UpgradeCost => Bonuses ? 500_000 : 5_000;

        /// <summary>False with "Looks and sound only" on: infused guns keep their camo, sounds and tracers, not the bonuses.</summary>
        internal static bool Bonuses => LooksOnly == null || !LooksOnly.Value;

        private const string IdleState = "p_zm_pap_idle";
        private const string PowerOnState = "p_zm_pap_poweron";
        private const string TakeGunState = "p_zm_pap_take_gun";
        private const string WorkingState = "p_zm_pap_working";
        private const string EjectGunState = "p_zm_pap_eject_gun";

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        internal static ManualLogSource Log;
        private static PaPHideoutModulePlugin _instanceRef;

        // ---------- Settled values (only the volumes and the enemy roll are in F12) ----------

        // 115 Infused weapons (read by the patches)
        internal const float DamageMultiplier = 1.17f;      // 60 damage becomes 70; penetration unchanged
        internal const float FireRateMultiplier = 1.12f;
        internal const float RecoilReduction = 0.06f;       // 6% less vertical/back recoil, counted like an attachment's
        internal const float HorizontalRecoilReduction = 0.5f;
        internal const bool Tracers = true;
        internal static readonly Color TracerColor = new Color(1f, 0.1f, 0.55f, 1f);
        internal const bool RedMuzzleFlash = true;
        internal static readonly Color MuzzleFlashColor = new Color(1f, 0.08f, 0.08f, 1f);
        internal const string NameSuffix = "(115 Infused)";
        internal const string ShortNameSuffix = "115";
        // A Pack-a-Punched M1911A1 firing grenades (Mustang & Sally) is named for it instead
        internal const string SallyNameSuffix = "(SALLY)";
        internal const string SallyShortNameSuffix = "SALLY";
        // Its grenades: out of the barrel at 1.5x the launcher's 76 m/s (flatter and further), a blast reaching 1.3x as
        // far (7 m becomes ~9 m) and the hand grenade's bigger explosion
        internal const float SallySpeedFactor = 1.5f;
        internal const float SallyBlastScale = 1.3f;
        internal const string SallyExplosionEffect = "Grenade_new";
        internal const float CamoTiling = 1f;
        internal static readonly Color GlowColor = new Color(0.68f, 0.3f, 1f, 1f);
        internal const float GlowLapSeconds = 4f;
        internal const float FillOpacity = 0.22f;
        internal const float AreaIconSize = 0.9f;

        // Distances (the volumes are sliders)
        internal const float RockHumDistance = 8f;          // Factory fragments
        internal const float HideoutRockHumDistance = 3f;   // the green hideout rocks: up close only

        // Where the machine stands, and how big
        private static readonly Vector3 SavedPosition = new Vector3(12.95f, 0f, 3.34f);
        private static readonly Vector3 SavedRotation = new Vector3(0f, 268.54f, 0f);
        private const float SavedScale = 0.35f;
        private const string CollisionLayer = "LowPolyCollider";

        // The machine's sequence
        private const float ReadyAt = 4f;            // gun back out on the ding of the sting
        private const float GrabWindow = 10f;        // then the machine keeps it
        private const float InteractDistance = 2.5f;
        private const float IdleSpinSpeed = 40f, WorkingSpinSpeed = 540f;
        private const float JingleMinInterval = 90f, JingleMaxInterval = 240f;
        private const float MaxDistance = 15f;

        // ---------- F12: audio levels and the enemy roll ----------

        internal static ConfigEntry<float> RockHumVolume;
        internal static ConfigEntry<float> RockPickupVolume;
        internal static ConfigEntry<float> HideoutRockHumVolume;
        internal static ConfigEntry<float> SongVolume;
        internal static ConfigEntry<float> ShotSoundVolume;
        internal static ConfigEntry<float> VoiceVolume;
        internal static ConfigEntry<bool> EnemyPaP;
        internal static ConfigEntry<bool> LooksOnly;
        internal static ConfigEntry<bool> MustangAndSally;
        private ConfigEntry<float> _ambientVolume;
        private ConfigEntry<float> _workingVolume;
        private ConfigEntry<float> _jingleVolume;
        private ConfigEntry<float> _stingVolume;
        private ConfigEntry<float> _crunchVolume;
        private ConfigEntry<float> _powerOnVolume;
        private ConfigEntry<float> _denyVolume;
        private ConfigEntry<float> _acceptVolume;

        /// <summary>Where the machine (and its hideout area's icon, camera and trigger) stands.</summary>
        internal static Vector3 MachinePosition => SavedPosition;
        internal static Quaternion MachineRotation => Quaternion.Euler(SavedRotation);

        // Off: built and the hideout has power, but nobody has switched the machine on yet (it blinks now and then)
        private enum MachineState { Unknown, Covered, Unpowered, Off, Powered }

        // Switched on by the player; a power cut switches it off again. Kept across hideout visits this session
        private static bool _switchedOn;

        private bool _loading;
        private int _seenRegistryVersion;
        private float _nextCamoFlush;

        private const float MovedRefreshDelay = 0.15f;
        private static volatile bool _itemsMoved;
        private float _movedRefreshAt = -1f;

        /// <summary>Something in an inventory moved (any thread): camo visuals get re-checked shortly.</summary>
        internal static void MarkItemsMoved() => _itemsMoved = true;
        private static bool _builtReported;

        private GameObject _instance;

        /// <summary>The spawned machine (and its tarp), for the area hover outline; null outside the hideout.</summary>
        internal static Transform Machine;
        private Animator _animator;
        private Animator _flagAnimator;
        private Transform[] _rollers = new Transform[0];
        private Renderer[] _machineRenderers = new Renderer[0];
        private AudioSource _jingle, _processLoop, _sting, _crunch, _powerOn;
        private readonly List<(Material material, Color emission)> _glowing = new List<(Material, Color)>();
        private Light[] _lights = new Light[0];
        private float[] _lightIntensities = new float[0];
        private GameObject _tarp;
        private MachineState _state;
        private float _nextStateCheck;

        private bool _busy;
        // The gun's fingerprint when the upgrade was asked for, to see whether the server's reply changed it
        private int _signatureBefore;
        private bool _working;
        private PaPDisplayGun _displayGun;
        private bool _canTake, _takeRequested;
        private int _takeSecondsLeft;
        private float _spinSpeed, _spinTarget, _spinAngle;
        private float _nextJingleTime;

        private readonly PaPInteractionMenu _menu = new PaPInteractionMenu();

        private void Awake()
        {
            Log = Logger;
            _instanceRef = this;
            PaPAssets.BundlePath = Path.Combine(Path.GetDirectoryName(Info.Location), PaPAssets.BundleFileName);
            BindConfig();

            var harmony = new Harmony(PluginGuid);
            PaPPatches.Apply(harmony);

            PaPRegistry.RefreshAsync();
            StartCoroutine(PaPAssets.PreloadAsync());
            Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void BindConfig()
        {
            const string audio = "1. Volume";
            _ambientVolume = Config.Bind(audio, "Machine hum", 0.1f, Volume("The running machine's hum while idle."));
            _workingVolume = Config.Bind(audio, "Machine working hum", 0.15f, Volume("The hum while a gun is being upgraded."));
            _jingleVolume = Config.Bind(audio, "Jingle", 0.2f, Volume("The Pack-a-Punch jingle the machine plays now and then."));
            _stingVolume = Config.Bind(audio, "Sting", 0.2f, Volume("The sting when a gun goes into the machine."));
            _crunchVolume = Config.Bind(audio, "Crunch", 0.2f, Volume("The crunch when a gun goes into the machine."));
            _powerOnVolume = Config.Bind(audio, "Power-on", 0.25f, Volume("The machine's start-up when you turn it on."));
            _denyVolume = Config.Bind(audio, "Deny", 0.5f, Volume("BO3's buzz when the machine refuses (not enough money)."));
            _acceptVolume = Config.Bind(audio, "Purchase", 0.5f, Volume("BO3's \"cha-ching\" when an upgrade is paid for."));
            VoiceVolume = Config.Bind(audio, "Voice line", 1f, Volume("Your character's \"Good work\" as the gun goes in (on top of the game's own)."));
            ShotSoundVolume = Config.Bind(audio, "Pack-a-Punch shot", 0.35f, Volume("The BO3 Pack-a-Punch layer on shots from 115 Infused guns."));
            RockHumVolume = Config.Bind(audio, "Factory 115 rock hum", 0.05f, Volume("The hum of the 115 fragments on Factory."));
            HideoutRockHumVolume = Config.Bind(audio, "Hideout green rock hum", 0.25f, Volume("The hum of the green 115 rocks hidden in the hideout."));
            RockPickupVolume = Config.Bind(audio, "115 rock pickup", 0.1f, Volume("The sound of picking up (Factory) or investigating (hideout) a 115 rock."));
            SongVolume = Config.Bind(audio, "115 song", 0.5f, Volume("The song the hideout plays once the green rocks are investigated."));

            const string gameplay = "2. Gameplay";
            LooksOnly = Config.Bind(gameplay, "Looks and sound only (no weapon bonuses)", false,
                "Pack-a-Punched guns keep the camo, glow, sounds, tracers and muzzle flash, but not the damage, fire rate and recoil bonuses. " +
                "The machine then costs 5,000 roubles, and an infused gun sells for 5,000 more instead of 500,000.");
            EnemyPaP = Config.Bind(gameplay, "Enemies can carry a 115 gun", true,
                "Very rarely, an enemy (scav, PMC, raider, boss, goon...) spawns with a Pack-a-Punched gun. Kill them and bring it home and it stays 115 Infused.");
            // An ode: BO1's Pack-a-Punched M1911 was Mustang & Sally, a pair; Tarkov only gets the one
            MustangAndSally = Config.Bind(gameplay, "Sally no Mustang (CHEAT)", false,
                "CHEAT. Turns projectiles into grenades, like good old times. A Pack-a-Punched M1911A1 still loads .45 ACP, but every shot " +
                "leaves the barrel as a 40mm M381 HE grenade: it arcs like one and explodes on impact. Mind the blast up close.");

            // Volumes apply live from the F12 menu
            Config.SettingChanged += (_, args) =>
            {
                if (args.ChangedSetting.Definition.Section == audio)
                    ApplyAudioSettings();
            };
        }

        private static ConfigDescription Volume(string description) =>
            new ConfigDescription(description, new AcceptableValueRange<float>(0f, 1f));

        private bool _camoReadySeen;

        private void Update()
        {
            if (PaPRegistry.Version != _seenRegistryVersion)
            {
                _seenRegistryVersion = PaPRegistry.Version;
                RefreshInfusedVisuals();
                PaPApi.RaiseChanged();
            }
            // Icons drawn before the camo texture loaded were drawn plain; redraw them once it's there
            else if (!_camoReadySeen && !PaPAssets.Preloading && PaPAssets.CamoReady)
            {
                _camoReadySeen = true;
                RefreshInfusedVisuals();
            }
            // Items moved: a short wait lets the move (and the cells it builds) settle, then pictures and models whose
            // camo state changed are redone. Only while camo is in play
            else if (_itemsMoved)
            {
                if (_movedRefreshAt < 0f)
                    _movedRefreshAt = Time.unscaledTime + MovedRefreshDelay;
                else if (Time.unscaledTime >= _movedRefreshAt)
                {
                    _itemsMoved = false;
                    _movedRefreshAt = -1f;
                    if (PaPRegistry.AnyInfused || CamoViews.Count > 0)
                        RefreshInfusedVisuals();
                }
            }

            if (Time.unscaledTime >= _nextCamoFlush)
            {
                _nextCamoFlush = Time.unscaledTime + 2f;
                // Until the first successful fetch (the game may start before the server knows the session),
                // keep asking, so stash icons don't render plain before the first hideout visit
                if (!PaPRegistry.Loaded)
                    PaPRegistry.RefreshAsync();
                KeepRaidHandsPainted();
                // A raid starts the hideout's green rocks over for the next visit; after it, enemy 115 guns brought
                // home are reported
                if (Singleton<AbstractGame>.Instance?.InRaid == true)
                    PaPHideoutRocks.ResetSession();
                else
                    PaPRegistry.FlushEnemyGuns();
            }

            // Cheap per-frame check: the owner is handed to us by the HideoutPlayerOwner.Init patch, no scene scans
            bool inHideout = InHideout;
            if (inHideout && _instance == null && !_loading)
                StartCoroutine(SpawnRoutine());
            else if (!inHideout && _instance != null)
                Despawn();

            if (_instance == null)
                return;

            UpdateState();
            SyncAliasesNow();
            _menu.Tick(Owner, _instance.transform, InteractDistance, GetActions);
            PaPHideoutRocks.Tick(Owner);

            UpdateJingle();
            UpdateOffBlink();
            UpdateLitSurge();
        }

        private void LateUpdate()
        {
            if (_instance == null || (_spinSpeed == 0f && _spinTarget == 0f) || !MachineVisible())
                return;

            // Runs after the Animator has posed the bones, so the spin layers on top of the BO3 anims
            _spinSpeed = Mathf.MoveTowards(_spinSpeed, _spinTarget, 720f * Time.deltaTime);
            _spinAngle = Mathf.Repeat(_spinAngle + _spinSpeed * Time.deltaTime, 360f);
            for (int i = 0; i < _rollers.Length; i++)
            {
                if (_rollers[i] != null)
                    _rollers[i].localRotation *= Quaternion.AngleAxis(i == 0 ? _spinAngle : -_spinAngle * 1.6f, Vector3.right);
            }
        }

        private bool MachineVisible()
        {
            foreach (Renderer r in _machineRenderers)
            {
                if (r != null && r.isVisible)
                    return true;
            }
            return false;
        }

        // Handed over by the HideoutPlayerOwner.Init patch; Unity's null check makes it go null once destroyed
        private static HideoutPlayerOwner Owner => PaPPatches.CurrentOwner;

        private static bool InHideout
        {
            get
            {
                HideoutPlayerOwner owner = Owner;
                return owner != null && owner.gameObject.activeInHierarchy && owner.HideoutPlayer != null;
            }
        }

        // ---------- Area state: covered (not built), unpowered, powered ----------

        private static MachineState DesiredState()
        {
            AreaData area = PaPArea.Data;
            if (area == null)
                return MachineState.Powered; // area unavailable (e.g. guest hideout): just a working machine
            if (area.CurrentLevel < 1)
                return MachineState.Covered;
            if (!PaPArea.PowerOn)
                return MachineState.Unpowered;
            return _switchedOn ? MachineState.Powered : MachineState.Off;
        }

        private void UpdateState()
        {
            if (Time.unscaledTime < _nextStateCheck)
                return;
            _nextStateCheck = Time.unscaledTime + 0.25f;

            AreaData area = PaPArea.Data;
            if (area != null && area.CurrentLevel >= 1 && !_builtReported)
            {
                // Mechanic's message the first time the area is seen built (the server only sends it once ever)
                _builtReported = true;
                PaPServer.Send(PaPServer.Built, "", _ => { });
            }

            MachineState want = DesiredState();
            // A power cut switches the machine off: when power comes back it waits to be switched on again
            if (want == MachineState.Unpowered)
                _switchedOn = false;
            // A gun inside the machine finishes its trip even if the power drops
            if (want == _state || _busy)
                return;

            MachineState was = _state;
            _state = want;
            Log.LogInfo($"Pack-a-Punch: {was} -> {want}.");

            bool powered = want == MachineState.Powered;
            // Switched on by the player: it blinks to life in PowerOnRoutine; arriving to a running one, it's just on
            bool switchingOn = powered && was == MachineState.Off;
            SetLit(powered && !switchingOn);
            _blinkLit = false;
            SetTarp(want == MachineState.Covered);
            if (powered)
            {
                StartCoroutine(PowerOnRoutine(switchingOn));
            }
            else
            {
                // Dead: still, dark and silent
                PlayFlag("flag_hidden");
                PlayMachine(IdleState);
                if (_animator != null)
                    _animator.speed = 0f;
                _spinTarget = _spinSpeed = 0f;
                if (_jingle != null)
                    _jingle.Stop();
                ApplyAudioSettings();
            }
        }

        /// <summary>Lights and glowing materials on or off; <paramref name="strength"/> above 1 is a brighter surge.</summary>
        private void SetLit(bool lit, float strength = 1f)
        {
            foreach (var (material, emission) in _glowing)
            {
                if (material != null)
                    material.SetColor(EmissionColor, lit ? emission * strength : Color.black);
            }
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] == null)
                    continue;
                _lights[i].enabled = lit;
                _lights[i].intensity = _lightIntensities[i] * strength;
            }
        }

        private float _nextSurge = -1f, _surgeUntil;
        private int _surgePulses;
        private bool _surging;

        /// <summary>
        /// Running: the lights now and then surge brighter for a moment, one to three quick pulses every few seconds,
        /// like the 115 inside catching. Back to normal in between.
        /// </summary>
        private void UpdateLitSurge()
        {
            // Not while the start-up flicker or the off blinks own the lights
            if (_state != MachineState.Powered || _busy)
            {
                if (_surging && _state == MachineState.Powered)
                    SetLit(true); // cut off mid-pulse: back to normal brightness
                _surging = false;
                _surgePulses = 0;
                _nextSurge = -1f;
                return;
            }
            if (_nextSurge < 0f)
                _nextSurge = Time.time + Random.Range(2f, 6f);
            if (!_surging && Time.time >= _nextSurge)
            {
                if (_surgePulses <= 0)
                    _surgePulses = Random.Range(1, 4); // a new surge: how many pulses it has
                _surging = true;
                _surgeUntil = Time.time + Random.Range(0.05f, 0.15f);
                SetLit(true, Random.Range(2f, 2.8f));
            }
            else if (_surging && Time.time >= _surgeUntil)
            {
                SetLit(true);
                _surging = false;
                // A short normal-brightness gap before the next pulse, or a long rest after the last one
                _nextSurge = Time.time + (--_surgePulses > 0 ? Random.Range(0.06f, 0.18f) : Random.Range(2f, 6f));
            }
        }

        // Alternating on/off times (s) of the lights catching as the machine powers up, starting with on
        private static readonly float[] LifeFlicker = { 0.07f, 0.15f, 0.05f, 0.3f, 0.12f, 0.08f, 0.06f, 0.22f, 0.35f, 0.1f };

        private IEnumerator FlickerToLife()
        {
            bool lit = false;
            foreach (float seconds in LifeFlicker)
            {
                lit = !lit;
                SetLit(lit);
                yield return new WaitForSeconds(seconds);
            }
            SetLit(_state == MachineState.Powered);
        }

        private float _nextBlink, _blinkUntil;
        private bool _blinkLit;

        /// <summary>
        /// Off, or the hideout's power is off: the lights catch now and then, slowly and at random, sometimes twice,
        /// like a machine waiting to be switched on.
        /// </summary>
        private void UpdateOffBlink()
        {
            // Also with the hideout's power switched off: the machine flickers on what's left in it
            if ((_state != MachineState.Off && _state != MachineState.Unpowered) || _busy)
                return;
            if (Time.time >= _nextBlink)
            {
                _blinkUntil = Time.time + Random.Range(0.06f, 0.4f);
                bool twice = Random.value < 0.35f;
                _nextBlink = _blinkUntil + (twice ? Random.Range(0.1f, 0.3f) : Random.Range(0.6f, 2.5f));
            }
            bool lit = Time.time < _blinkUntil;
            if (lit != _blinkLit)
            {
                _blinkLit = lit;
                SetLit(lit);
            }
        }

        private void SetTarp(bool covered)
        {
            if (covered && _tarp == null)
                _tarp = PaPTarp.Build(_instance);
            if (_tarp != null)
                _tarp.SetActive(covered);
        }

        private IEnumerator PowerOnRoutine(bool withSound)
        {
            _busy = true;
            if (_animator != null)
                _animator.speed = 1f;
            PlayFlag("flag_hidden");
            if (withSound && _powerOn != null)
            {
                if (_powerOn.clip == null)
                    _powerOn.clip = PaPAssets.PowerOnClip;
                if (_powerOn.clip != null)
                    _powerOn.Play();
                else
                    Log.LogWarning("Power-on sound not found in the bundle.");
            }
            if (withSound)
                StartCoroutine(FlickerToLife());
            ApplyAudioSettings(); // hum back on
            _spinTarget = IdleSpinSpeed;
            yield return PlayAndWait(PowerOnState);
            PlayMachine(IdleState);
            // The first jingle comes soon after power-on, so a visit actually hears it; then the usual gaps
            _nextJingleTime = Time.time + Random.Range(20f, 60f);
            _busy = false;
        }

        // ---------- Interaction menu ----------

        /// <summary>Options shown in EFT's interaction menu; a null action is shown greyed out and can't be used.</summary>
        private IEnumerable<(string name, Action action)> GetActions()
        {
            HideoutPlayerOwner owner = Owner;
            if (_canTake)
            {
                yield return ($"Take weapon ({_takeSecondsLeft})", () => _takeRequested = true);
                yield break;
            }
            switch (_state)
            {
                case MachineState.Covered:
                    yield return ("Covered by an old tarp", null);
                    yield break;
                case MachineState.Unpowered:
                    yield return ("No power", null);
                    yield break;
                case MachineState.Off:
                    yield return ("Turn on", () => _switchedOn = true);
                    yield break;
            }
            if (_busy)
            {
                yield return ("The machine is busy...", null);
                yield break;
            }
            if (!owner.InShootingRange)
            {
                yield return ("Draw a weapon", () => PaPUncensored.SetArmed(true));
                yield break;
            }
            if (owner.HideoutPlayer.IsUpdateHideoutPlayerInventoryInProgress)
            {
                yield return ("Drawing weapons...", null);
                yield break;
            }

            Weapon weapon = HeldWeapon(owner);
            if (weapon == null)
                yield return ("Draw a weapon to insert it", null);
            else if (PaPRegistry.IsInfused(weapon.Id))
                yield return ("Already 115 Infused", null);
            else
                yield return ($"Insert gun ({UpgradeCost:N0} RUB)", TryUpgradeHeldWeapon);
        }

        private static Weapon HeldWeapon(HideoutPlayerOwner owner) => owner?.HideoutPlayer?.HandsController?.Item as Weapon;

        // ---------- Weapons-mode copies ----------

        private static int _copySignature;

        /// <summary>
        /// Weapons out in the hideout means holding a copy of the gear with new ids; map it back so the real weapon
        /// gets upgraded and the copy shows camo, name and stats. Runs as soon as the copy exists (also straight from
        /// the camo hook, before the drawn gun's model is painted), so the camo is on from the first frame of the draw.
        /// Old pairs are kept: they cost nothing and the copy's models can outlive weapons mode.
        /// </summary>
        internal static void SyncAliasesNow()
        {
            HideoutPlayer player = Owner?.HideoutPlayer;
            Inventory copy = player?.ShootingRangeInventory?.Inventory;
            if (copy == null)
                return;

            int signature = 17;
            foreach (Slot slot in copy.Equipment.GetAllSlots())
                signature = signature * 31 + (slot.ContainedItem?.Id?.GetHashCode() ?? 0);
            if (signature == _copySignature)
                return;
            _copySignature = signature;
            PaPRegistry.AddAliases(CopyToOriginalIds(player).ToList());
        }

        /// <summary>Pairs every item of the weapons-mode equipment copy with its original, by slot and position.</summary>
        private static IEnumerable<KeyValuePair<string, string>> CopyToOriginalIds(HideoutPlayer player)
        {
            Inventory copy = player.ShootingRangeInventory?.Inventory;
            Inventory original = player.OriginalInventory?.Inventory;
            if (copy == null || original == null)
                yield break;

            foreach (Slot copySlot in copy.Equipment.GetAllSlots())
            {
                Item copyRoot = copySlot.ContainedItem;
                Item originalRoot = original.Equipment.GetAllSlots().FirstOrDefault(s => s.ID == copySlot.ID)?.ContainedItem;
                if (copyRoot == null || originalRoot == null || copyRoot.TemplateId != originalRoot.TemplateId)
                    continue;
                if (copyRoot.Id != originalRoot.Id)
                    yield return new KeyValuePair<string, string>(copyRoot.Id, originalRoot.Id);

                // Trader-built guns can carry differently split ammo in the copy, so parts are matched without loose ammo
                List<Item> copies = copyRoot.GetAllItems().Skip(1).Where(i => !(i is Ammo)).ToList();
                List<Item> originals = originalRoot.GetAllItems().Skip(1).Where(i => !(i is Ammo)).ToList();
                if (copies.Count != originals.Count)
                    continue;
                for (int i = 0; i < copies.Count; i++)
                {
                    if (copies[i].TemplateId == originals[i].TemplateId && copies[i].Id != originals[i].Id)
                        yield return new KeyValuePair<string, string>(copies[i].Id, originals[i].Id);
                }
            }
        }

        // ---------- Upgrade ----------

        private void TryUpgradeHeldWeapon()
        {
            Weapon weapon = HeldWeapon(Owner);
            if (weapon == null || PaPRegistry.IsInfused(weapon.Id))
                return;

            // The hands hold the weapons-mode copy; the server only knows the original in the profile
            string weaponId = PaPRegistry.OriginalId(weapon.Id);
            Weapon original = Owner.HideoutPlayer.OriginalInventory?.Inventory?.Equipment?.GetAllItems().FirstOrDefault(i => i.Id == weaponId) as Weapon;
            if (original == null)
            {
                Warn("The machine can't find that weapon in your equipment.");
                Log.LogWarning($"No original found for held weapon {weapon.Id} (mapped to {weaponId})");
                return;
            }

            _busy = true;
            Log.LogInfo($"Requesting Pack-a-Punch for {weapon.LocalizedName()} (held {weapon.Id}, original {weaponId})");
            // Plain until it comes back out of the machine (released in UseRoutine)
            PaPCamo.Hold(original);
            _signatureBefore = PaPCopies.Signature(weaponId);
            PaPServer.SendUpgrade(weaponId, !Bonuses, result =>
            {
                // The server's list is the truth: it only contains the weapon if the fee was paid
                PaPRegistry.Refresh();
                if (PaPRegistry.IsInfused(weaponId))
                {
                    // Paid: BO's cha-ching, then the machine takes the gun
                    if (_accept != null && _accept.clip != null)
                        _accept.Play();
                    // A server mod that changed the gun (PaPServerApi) had the reply replace the profile's item with a
                    // new one, same id: carry on with that one
                    StartCoroutine(UseRoutine(PaPCopies.Real(weaponId) ?? original));
                    return;
                }

                PaPCamo.Release(weaponId);
                _busy = false;
                if (_deny != null && _deny.clip != null)
                    _deny.Play();
                string reason = result == null ? "It can't reach the server."
                    : result.Failed && !string.IsNullOrEmpty(result.Error) ? result.Error
                    : $"You need {UpgradeCost:N0} roubles.";
                Warn($"The machine refuses. {reason}");
            });
        }

        private static void Warn(string message) =>
            NotificationManager.DisplayWarningNotification(message, ENotificationDurationType.Default);

        private static void Notify(string message) =>
            NotificationManager.DisplayMessageNotification(message, ENotificationDurationType.Long, ENotificationIconType.Default, null);

        // Inventory cells whose picture shows (or showed) the camo
        private static readonly HashSet<ItemView> CamoViews = new HashSet<ItemView>();

        internal static void TrackIconView(ItemView view) => CamoViews.Add(view);

        private static void RefreshInfusedVisuals()
        {
            // Only the views/models the hooks have already seen, instead of scanning the whole scene
            PaPCamo.RefreshAll();
            CamoViews.RemoveWhere(v => v == null);
            foreach (ItemView view in PaPPatches.LiveItemViews())
            {
                Item item = view.Item;
                PaPGlow.Attach(view, (item is Weapon && PaPRegistry.IsInfused(item.Id)) || PaPFragments.Is(item));

                // A new camo state means a new icon hash: re-request the picture, but only where camo is involved
                // Fragments too: a picture taken before the rocks could load showed no rock
                bool camo = item != null && (PaPFragments.Is(item) || PaPCamo.OnInfusedGun(item));
                if (!camo && !CamoViews.Contains(view))
                    continue;
                if (camo)
                    CamoViews.Add(view);
                else
                    CamoViews.Remove(view);
                try
                {
                    view.RefreshIcon();
                }
                catch (Exception e)
                {
                    Log.LogWarning($"Refreshing the icon of {item?.LocalizedName()} failed: {e.Message}");
                }
            }
        }

        // ---------- Hideout lifecycle ----------

        private IEnumerator SpawnRoutine()
        {
            _loading = true;
            try
            {
                PaPRegistry.RefreshAsync();
                while (PaPAssets.Preloading)
                    yield return null;

                GameObject prefab = PaPAssets.MachinePrefab;
                if (prefab != null && InHideout)
                    Spawn(prefab);
            }
            finally
            {
                _loading = false;
            }
        }

        private void Spawn(GameObject prefab)
        {
            _instance = Object.Instantiate(prefab);
            _instance.name = InstanceName;
            Machine = _instance.transform;
            _instance.transform.localScale = Vector3.one * SavedScale;
            _instance.transform.SetPositionAndRotation(SavedPosition, Quaternion.Euler(SavedRotation));

            FixShaders(_instance);
            SetupColliders();
            SetupAnimators();
            SetupAudio();
            SetupGlow();

            _busy = _working = false;
            _state = MachineState.Unknown;
            _nextStateCheck = 0f;
            _spinSpeed = _spinTarget = 0f;
            ScheduleNextJingle();

            Vector3 p = _instance.transform.position;
            Log.LogInfo($"Spawned '{InstanceName}' at ({p.x:F3}, {p.y:F3}, {p.z:F3}).");
        }

        private void Despawn()
        {
            StopAllCoroutines();
            _menu.Reset();
            ClearDisplayGun();
            Object.Destroy(_instance);
            _instance = null;
            _tarp = null;
            _animator = _flagAnimator = null;
            _rollers = new Transform[0];
            _machineRenderers = new Renderer[0];
            _lights = new Light[0];
            _lightIntensities = new float[0];
            _glowing.Clear();
            _jingle = _processLoop = _sting = _crunch = _powerOn = _deny = _accept = null;
            _canTake = _takeRequested = false;
            _busy = _working = false;
            _state = MachineState.Unknown;
            _copySignature = 0;
            PaPCamo.ReleaseAll();
            PaPHideoutRocks.Despawn();
            Machine = null;
            // Aliases are kept: the raid loading screen's character is built from a fresh clone while the hideout is
            // still up, and clearing here (with the refresh it triggers) stripped the camo off its gun
            Log.LogInfo("Left Hideout; machine despawned.");
        }

        // ---------- Setup ----------

        private void SetupColliders()
        {
            int layer = LayerMask.NameToLayer(CollisionLayer);
            if (layer < 0)
            {
                Log.LogWarning($"Layer '{CollisionLayer}' not found; colliders stay on Default.");
                return;
            }
            foreach (Collider c in _instance.GetComponentsInChildren<Collider>(true))
                c.gameObject.layer = layer;
        }

        private void SetupAnimators()
        {
            Animator[] animators = _instance.GetComponentsInChildren<Animator>(true);
            _animator = animators.FirstOrDefault(a => a.gameObject == _instance);
            _flagAnimator = animators.FirstOrDefault(a => a.gameObject.name == "pap_flag");
            foreach (Animator a in animators)
            {
                a.enabled = true;
                // Nobody needs the machine posed while it's off screen
                a.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                a.applyRootMotion = false;
            }

            _machineRenderers = _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer smr in _machineRenderers.OfType<SkinnedMeshRenderer>())
                smr.updateWhenOffscreen = false;

            Transform[] all = _instance.GetComponentsInChildren<Transform>(true);
            _rollers = new[] { "j_rollers_large", "j_rollers_small" }
                .Select(n => all.FirstOrDefault(t => t.name == n))
                .ToArray();
        }

        private void SetupGlow()
        {
            // The sign's emission and light, remembered so the machine can go dark without power
            _glowing.Clear();
            foreach (Renderer renderer in _instance.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.materials)
                {
                    if (material != null && material.HasProperty(EmissionColor) && material.GetColor(EmissionColor).maxColorComponent > 0.001f)
                        _glowing.Add((material, material.GetColor(EmissionColor)));
                }
            }
            _lights = _instance.GetComponentsInChildren<Light>(true);
            foreach (Light light in _lights)
            {
                light.range *= SavedScale;
                // The sign's bulb light: warm yellow, as the BO3 machine's (the bundle has it pinkish white)
                if (light.name == "SignLight")
                    light.color = SignLightColor;
            }
            _lights = _lights.Append(AddInnerLight()).Where(l => l != null).ToArray();
            _lightIntensities = _lights.Select(l => l.intensity).ToArray();
        }

        /// <summary>
        /// A soft white light just behind the rollers, so the inside of the running machine has some life. A Unity
        /// light has no visible source; only what it lights is seen. It goes on and off with the machine's own lights.
        /// </summary>
        private static readonly Color SignLightColor = new Color(1f, 0.8f, 0.42f);

        private Light AddInnerLight()
        {
            Transform roller = _rollers.FirstOrDefault(r => r != null);
            if (roller == null)
                return null;
            var go = new GameObject("PaP_InnerLight");
            go.transform.SetParent(_instance.transform, false);
            // Behind the rollers, as seen from the front (the machine faces its forward axis)
            go.transform.position = roller.position - _instance.transform.forward * 0.15f;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.intensity = 1.2f;
            light.range = 1f; // metres in the world: a light's range ignores the machine's scale
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }

        private void SetupAudio()
        {
            AudioSource[] sources = _instance.GetComponentsInChildren<AudioSource>(true);
            AudioSource Find(string name) => sources.FirstOrDefault(s => s.gameObject.name == name);
            _jingle = Find("Audio_Jingle");
            _processLoop = Find("Audio_ProcessLoop");
            _sting = Find("Audio_Sting");
            _crunch = Find("Audio_Crunch");

            var power = new GameObject("Audio_PowerOn");
            power.transform.SetParent(_instance.transform, false);
            power.transform.localPosition = Vector3.up * 2f;
            _powerOn = power.AddComponent<AudioSource>();
            _powerOn.clip = PaPAssets.PowerOnClip;

            var deny = new GameObject("Audio_Deny");
            deny.transform.SetParent(_instance.transform, false);
            deny.transform.localPosition = Vector3.up * 1.2f;
            _deny = deny.AddComponent<AudioSource>();
            _deny.clip = PaPAssets.DenyClip;

            var accept = new GameObject("Audio_Accept");
            accept.transform.SetParent(_instance.transform, false);
            accept.transform.localPosition = Vector3.up * 1.2f;
            _accept = accept.AddComponent<AudioSource>();
            _accept.clip = PaPAssets.AcceptClip;
            ApplyAudioSettings();
        }

        private AudioSource _deny, _accept;

        private void ApplyAudioSettings()
        {
            foreach (AudioSource source in new[] { _jingle, _processLoop, _sting, _crunch, _powerOn, _deny, _accept })
            {
                if (source == null)
                    continue;
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 1f;
                source.maxDistance = MaxDistance;
                source.dopplerLevel = 0f;
                source.volume = source == _jingle ? _jingleVolume.Value
                    : source == _sting ? _stingVolume.Value
                    : source == _crunch ? _crunchVolume.Value
                    : source == _powerOn ? _powerOnVolume.Value
                    : source == _deny ? _denyVolume.Value
                    : source == _accept ? _acceptVolume.Value
                    : _working ? _workingVolume.Value
                    : _ambientVolume.Value;
            }

            bool powered = _state == MachineState.Powered;
            if (_jingle != null)
            {
                _jingle.loop = false;
                if (!powered && _jingle.isPlaying)
                    _jingle.Stop();
            }
            if (_processLoop != null)
            {
                _processLoop.loop = true;
                bool hum = powered;
                if (hum && !_processLoop.isPlaying)
                    _processLoop.Play();
                else if (!hum && _processLoop.isPlaying)
                    _processLoop.Stop();
            }
        }

        private static void FixShaders(GameObject root)
        {
            // Only rebind bundled shaders that can't run here (pink); swapping working ones can lose keyword variants.
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material mat in renderer.materials)
                {
                    if (mat == null || mat.shader == null || mat.shader.isSupported)
                        continue;
                    Shader gameShader = Shader.Find(mat.shader.name);
                    if (gameShader != null)
                        mat.shader = gameShader;
                }
            }
        }

        // ---------- Use sequence ----------

        // Where the gun travels, in the machine's own (unscaled) space: in front of the mouth, inside at the
        // rollers, pushed back out for the player, and deep enough inside that the machine keeps it
        private static readonly Vector3 GunFront = new Vector3(0f, 2.15f, 2.0f);
        private static readonly Vector3 GunInside = new Vector3(0f, 2.2f, 0.0f);
        private static readonly Vector3 GunOut = new Vector3(0f, 2.15f, 0.9f);
        private static readonly Vector3 GunGone = new Vector3(0f, 2.2f, -0.7f);

        /// <summary>
        /// The BO3 use sequence. weapon is the original (profile) weapon that was just infused, or null for the
        /// debug key. The gun leaves the player's hands, rides into the machine, vanishes, comes back out with the
        /// camo, and creeps back in; take it in time or (if enabled) the machine keeps it.
        /// </summary>
        private IEnumerator UseRoutine(Weapon weapon)
        {
            _busy = true;
            _spinTarget = IdleSpinSpeed;
            if (_jingle != null)
                _jingle.Stop();

            // Gun goes in: sting plus a quiet crunch, flag up
            float stingStart = Time.time;
            PlayFlag("flag_up");
            if (_sting != null) _sting.Play();
            if (_crunch != null) _crunch.Play();

            // The gun leaves the hands but weapons mode stays on: the arms stay out, so the gesture is visible,
            // and the same gun goes straight back into them afterwards
            HideoutPlayerOwner owner = Owner;
            HideoutPlayer player = owner.HideoutPlayer;
            EquipmentSlot? slot = null;
            if (weapon != null && owner.InShootingRange)
            {
                slot = SlotOf(player.HandsController?.Item);
                player.SetEmptyHands(_ => { });
                // The display gun must not be built before the model on the arms is back in the pool
                float timeout = Time.time + 3f;
                while (Time.time < timeout && !(player.HandsController is Player.EmptyHandsController))
                    yield return null;
                // Out of the hands now: if a server mod changed the gun, the copy it goes back as is rebuilt to match
                PaPCopies.RefreshIfChanged(weapon.Id, _signatureBefore);
            }
            _signatureBefore = 0;

            // While the sting plays and the gun goes in: a gesture and a voice line (BO3's knuckle crack)
            PaPGestures.Play(owner);

            if (weapon != null)
            {
                _displayGun = new PaPDisplayGun(_instance.transform);
                yield return _displayGun.Load(weapon);
                _displayGun.SetLocalPosition(GunFront);
            }

            // Timed against the sting: whatever the hand-over took comes out of the working time,
            // so the gun is still back out and ready on its ding
            float take = Mathf.Max(1f, ClipLength(_animator, TakeGunState));
            float eject = Mathf.Max(1f, ClipLength(_animator, EjectGunState));

            PlayMachine(TakeGunState);
            yield return MoveGun(GunFront, GunInside, take);
            float work = Mathf.Max(0.5f, ReadyAt - (Time.time - stingStart) - eject);

            // Working: the hum swells while the rollers run fast; the gun hovers, then vanishes
            PlayMachine(WorkingState);
            _working = true;
            ApplyAudioSettings();
            _spinTarget = WorkingSpinSpeed;
            float hover = work * 0.5f;
            yield return Hover(GunInside, hover);
            _displayGun?.SetVisible(false);
            yield return new WaitForSeconds(work - hover);

            _spinTarget = IdleSpinSpeed;
            _working = false;
            ApplyAudioSettings();

            if (weapon == null)
            {
                yield return PlayAndWait(EjectGunState);
                FinishUse();
                yield break;
            }

            // Back out, now wearing the camo
            PaPCamo.Release(weapon.Id);
            RefreshInfusedVisuals();
            PaPApi.RaiseUpgraded(weapon);
            if (_displayGun != null)
            {
                _displayGun.ShowCamo(weapon, true);
                _displayGun.SetVisible(true);
            }
            PlayMachine(EjectGunState);
            yield return MoveGun(GunInside, GunOut, eject);

            // Grab window: the gun creeps back in; take it before it's gone
            _takeRequested = false;
            _canTake = true;
            float window = Mathf.Max(3f, GrabWindow);
            for (float t = 0f; t < window && !_takeRequested; t += Time.deltaTime)
            {
                _takeSecondsLeft = Mathf.CeilToInt(window - t);
                _displayGun?.SetLocalPosition(Vector3.Lerp(GunOut, GunGone, t / window), Mathf.Sin(Time.time * 2f) * 0.02f);
                yield return null;
            }
            _canTake = false;

            if (_takeRequested)
            {
                Vector3 spot = ((Component)player).transform.position;
                ClearDisplayGun();
                // Let the borrowed model settle back into the pool before the game builds the player's weapon
                yield return null;
                yield return null;
                if (slot.HasValue && owner.InShootingRange)
                    player.SetSlotItem(slot.Value, _ => { });
                else
                    PaPUncensored.DrawWith(owner, weapon);
                StartCoroutine(HoldInPlace(player, spot));
                Notify($"{weapon.LocalizedName()}: the machine hums with power.");
                Log.LogInfo($"{weapon.LocalizedName()} ({weapon.Id}) taken back, 115 Infused.");
            }
            else
            {
                ClearDisplayGun();
                if (_crunch != null) _crunch.Play();
                string name = weapon.LocalizedName();
                PaPServer.Send(PaPServer.Destroy, weapon.Id, result =>
                {
                    PaPRegistry.Refresh();
                    if (result != null && !result.Failed)
                        Warn($"You left your {name} in the machine. It's gone.");
                    else
                        Log.LogError($"Destroying {weapon.Id} failed: {result?.Error ?? "no response"}");
                });
            }

            FinishUse();
        }

        private static EquipmentSlot? SlotOf(Item item)
        {
            string id = (item?.CurrentAddress?.Container as Slot)?.ID;
            return id != null && Enum.TryParse(id, out EquipmentSlot slot) ? slot : (EquipmentSlot?)null;
        }

        /// <summary>
        /// Keeps the character where they took the gun until it's in their hands (at most 5 s), then lets go, so
        /// no leftover motion from the hand-over carries on as a slide. Nothing of it runs after that.
        /// </summary>
        private IEnumerator HoldInPlace(HideoutPlayer player, Vector3 spot)
        {
            float giveUp = Time.time + 5f;
            while (Time.time < giveUp)
            {
                ResetCharacter(player, spot);
                if (player != null && !player.IsUpdateHideoutPlayerInventoryInProgress && player.HandsController?.Item is Weapon)
                    break;
                yield return null;
            }

            // A few more frames once the gun is in hand, then let go
            for (int i = 0; i < 10; i++)
            {
                ResetCharacter(player, spot);
                yield return null;
            }
        }

        private static void ResetCharacter(HideoutPlayer player, Vector3 spot)
        {
            if (player == null)
                return;
            try
            {
                player.Teleport(spot, false);
                player.MovementContext?.ResetFlying();
                SimpleCharacterController controller = ((Component)player).GetComponentInChildren<SimpleCharacterController>();
                if (controller != null)
                    controller.velocity = Vector3.zero;
            }
            catch (Exception e)
            {
                Log.LogWarning($"Resetting the character failed: {e.Message}");
            }
        }

        private void FinishUse()
        {
            PlayFlag("flag_down");
            PlayMachine(IdleState);
            ScheduleNextJingle();
            _busy = false;
            _nextStateCheck = 0f; // pick up a power change that happened mid-sequence
        }

        private IEnumerator MoveGun(Vector3 from, Vector3 to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                _displayGun?.SetLocalPosition(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)));
                yield return null;
            }
            _displayGun?.SetLocalPosition(to);
        }

        private IEnumerator Hover(Vector3 at, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                _displayGun?.SetLocalPosition(at, Mathf.Sin(t * 4f) * 0.04f);
                yield return null;
            }
        }

        private void ClearDisplayGun()
        {
            _displayGun?.Dispose();
            _displayGun = null;
        }

        private void UpdateJingle()
        {
            if (_jingle == null || _busy || _state != MachineState.Powered || _jingle.isPlaying || Time.time < _nextJingleTime)
                return;
            _jingle.Play();
            _nextJingleTime = Time.time + _jingle.clip.length + Random.Range(JingleMinInterval, JingleMaxInterval);
        }

        private void ScheduleNextJingle() =>
            _nextJingleTime = Time.time + Random.Range(JingleMinInterval, JingleMaxInterval);

        private IEnumerator PlayAndWait(string state)
        {
            PlayMachine(state);
            yield return new WaitForSeconds(ClipLength(_animator, state));
        }

        private void PlayMachine(string state) => PlayState(_animator, state);

        private void PlayFlag(string state) => PlayState(_flagAnimator, state);

        private static void PlayState(Animator animator, string state)
        {
            if (animator == null)
                return;
            if (animator.HasState(0, Animator.StringToHash(state)))
                animator.Play(state, 0, 0f);
            else
                Log.LogWarning($"Animator '{animator.name}' has no state '{state}'.");
        }

        private static float ClipLength(Animator animator, string clipName)
        {
            AnimationClip clip = animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name == clipName)
                : null;
            return clip != null ? clip.length : 1f;
        }

        /// <summary>
        /// In raid, the gun in the player's hands can be built before the raid's inventory is matched to the infused
        /// list (it spawns plain), and a part put on or taken off can be modelled a moment before it's moved: every
        /// 2 s the held gun is checked against the camo rule (a material swap only where something changed).
        /// </summary>
        private static void KeepRaidHandsPainted()
        {
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null || player.HandsController?.Item is not Weapon weapon)
                return;
            GameObject model = player.HandsController.ControllerGameObject;
            if (model == null || (!PaPRegistry.IsInfused(weapon.Id) && !PaPCamo.IsPainted(model)))
                return;
            PaPCamo.Apply(model, weapon);
        }

        private void OnDestroy()
        {
            if (_instance != null)
                Object.Destroy(_instance);
            PaPAssets.Unload();
        }
    }
}
