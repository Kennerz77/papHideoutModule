using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaPHideoutModule
{
    /// <summary>
    /// "Pack-a-Punch" as a real hideout area (type 115): its own icon, requirements, construction and level, like
    /// the Gym or Hall of Fame. The server mod defines its stages and requirements; this side adds what the client
    /// needs for an area: a template (icon, behaviour, stages) and a HideoutArea object in the scene. The scene object
    /// is a copy of the Library's, parked far below the map with only its icon point, camera and trigger brought to
    /// the machine, so none of the Library's art shows up.
    /// </summary>
    internal static class PaPArea
    {
        public const EAreaType Type = (EAreaType)115;
        private const string AreaId = "6650a115a115a115a115a115";
        private const EAreaType Source = EAreaType.Library; // two levels and an empty behaviour

        private static AreaTemplate _template;

        /// <summary>Called before the game reads the area templates (HideoutAreasInfo.Templates).</summary>
        public static void InjectTemplate(HideoutAreasInfo info)
        {
            Dictionary<EAreaType, AreaTemplate> templates = info._areas;
            if (templates == null || templates.ContainsKey(Type))
                return;
            if (!templates.TryGetValue(Source, out AreaTemplate source) || source == null)
            {
                PaPHideoutModulePlugin.Log.LogError("No Library area template to base the Pack-a-Punch area on; the area is disabled.");
                return;
            }
            templates[Type] = _template != null ? _template : _template = BuildTemplate(source);
        }

        private static AreaTemplate BuildTemplate(AreaTemplate source)
        {
            AreaTemplate template = ScriptableObject.CreateInstance<AreaTemplate>();
            template.name = "PaP area";
            template.Id = AreaId;
            template.Type = Type;
            template.Enabled = false; // the server's template switches it on; without the server mod it stays hidden
            template.DisplayLevel = true;
            template.AreaBehaviour = new LibraryBehaviour();
            template.AreaIcon = PaPAssets.AreaIconLike(source.AreaIcon) ?? source.AreaIcon;
            template.CameraTimePosition = source.CameraTimePosition;
            template.DefaultSounds = source.DefaultSounds;
            foreach (var delay in source.PowerDelays)
                template.PowerDelays[delay.Key] = delay.Value;

            // Fresh stages (the server's data is copied into them), sharing only the Library's construction sounds
            template.Stages = source.Stages.ToDictionary(pair => pair.Key, pair => new Stage { Sounds = pair.Value.Sounds, HideoutProgress = 0f });
            template.Init();
            return template;
        }

        /// <summary>Called before the hideout scene sets up its areas (HideoutController.HideoutAwake).</summary>
        public static void InjectSceneArea(HideoutController controller, Vector3 position, Quaternion rotation)
        {
            Dictionary<EAreaType, HideoutArea> areas = controller._areas;
            if (_template == null || areas == null || areas.ContainsKey(Type))
                return;

            HideoutArea source = areas.TryGetValue(Source, out HideoutArea library) && library != null
                ? library
                : areas.Values.FirstOrDefault(a => a != null && a.AreaLevels != null && a.AreaLevels.Length >= 2);
            if (source == null)
            {
                PaPHideoutModulePlugin.Log.LogError("No hideout area object to base the Pack-a-Punch area on.");
                return;
            }

            GameObject copy = Object.Instantiate(source.gameObject, source.transform.parent);
            copy.name = "PaP_Area";
            copy.transform.position = source.transform.position + Vector3.down * 300f;
            foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (Light light in copy.GetComponentsInChildren<Light>(true))
                light.enabled = false;

            HideoutArea area = copy.GetComponent<HideoutArea>();
            area._areaTemplate = _template;
            Vector3 front = rotation * Vector3.forward;

            if (area._areaIconPoint != null)
                area._areaIconPoint.position = position + Vector3.up * 2.3f;

            Vector3 cameraAt = position + front * 3.2f + Vector3.up * 1.7f;
            Quaternion lookAt = Quaternion.LookRotation(position + Vector3.up * 1f - cameraAt);
            foreach (var camera in new[] { area._areaCamera, area._specialActionCamera })
            {
                if (camera == null)
                    continue;
                // Targets would still point at the Library down below; a fixed shot of the machine instead
                camera.Follow = null;
                camera.LookAt = null;
                camera.transform.SetPositionAndRotation(cameraAt, lookAt);
            }

            if (area._trigger != null)
            {
                Transform trigger = area._trigger.transform;
                trigger.SetPositionAndRotation(position + Vector3.up * 1.25f, rotation);
                Vector3 scale = trigger.lossyScale;
                foreach (BoxCollider box in trigger.GetComponents<BoxCollider>())
                {
                    box.center = Vector3.zero;
                    box.size = new Vector3(4f / scale.x, 2.5f / scale.y, 4f / scale.z);
                }
            }

            // The level "highlight" is what the game switches on and puffs construction dust at when a level is built:
            // give each level an empty one at the machine, so the dust appears there and nothing else is touched
            foreach (HideoutAreaLevel level in area.AreaLevels)
            {
                var highlight = new GameObject("PaP_Highlight").transform;
                highlight.SetParent(level.transform, false);
                highlight.position = position;
                var dust = new GameObject("PaP_Dust").transform;
                dust.SetParent(highlight, false);
                dust.position = position + Vector3.up * 0.8f;
                level._highlightTransform = highlight;
            }

            areas[Type] = area;
            PaPHideoutModulePlugin.Log.LogInfo($"Pack-a-Punch hideout area added (based on {source.name}).");
        }

        /// <summary>The area's live data, or null when the area isn't available (server mod missing, guest hideout...).</summary>
        public static AreaData Data
        {
            get
            {
                if (!Singleton<HideoutRepresentation>.Instantiated)
                    return null;
                AreaData data = Singleton<HideoutRepresentation>.Instance.AreaDatas.FirstOrDefault(a => a.Template.Type == Type);
                return data != null && data.Enabled && !data.IsGuestData ? data : null;
            }
        }

        public static bool PowerOn
        {
            get
            {
                try
                {
                    return !Singleton<HideoutRepresentation>.Instantiated
                           || Singleton<HideoutRepresentation>.Instance.EnergyController?.IsEnergyGenerationOn != false;
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }
    }
}
