using System.Collections.Generic;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using UnityEngine;
using UnityEngine.UI;

namespace PaPHideoutModule
{
    /// <summary>
    /// Blue light that runs round the edge of a Pack-a-Punched weapon's inventory cell, with a fading tail and
    /// soft glow (drawing adapted from Kappa Marker's QuestBeam). Sits as an extra child of the item view,
    /// stretched over it, ignored by layout and raycasts.
    /// </summary>
    public class PaPGlow : MaskableGraphic
    {
        private const string ObjectName = "PaPGlow";

        // distance between points along a fading strip, in canvas pixels
        private const float Step = 3f;
        private const float GlowStrength = 0.45f;

        private readonly List<float> _samples = new List<float>(256);

        private const string FillName = "PaPFill";

        private static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>();

        internal static void SetColor(string templateId, Color color)
        {
            if (!string.IsNullOrEmpty(templateId))
                Colors[templateId] = color;
        }

        private static Color ColorFor(Item item) =>
            item != null && Colors.TryGetValue(item.TemplateId.ToString(), out Color own) ? own : PaPHideoutModulePlugin.GlowColor;

        private Color _tint = PaPHideoutModulePlugin.GlowColor;

        internal static void Attach(ItemView itemView, bool infused)
        {
            if (itemView == null)
                return;

            SetFill(itemView, infused);
            Transform view = itemView.transform;
            Transform existing = view.Find(ObjectName);
            PaPGlow glow = existing != null ? existing.GetComponent<PaPGlow>() : null;

            // item views are pooled and reused for other items, so switch it back off
            if (!infused)
            {
                if (glow != null)
                    glow.gameObject.SetActive(false);
                return;
            }

            if (glow == null)
            {
                var go = new GameObject(ObjectName, typeof(RectTransform));
                go.layer = view.gameObject.layer;
                go.AddComponent<LayoutElement>().ignoreLayout = true;

                var rt = (RectTransform)go.transform;
                rt.SetParent(view, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                glow = go.AddComponent<PaPGlow>();
                glow.raycastTarget = false;
            }

            glow._tint = ColorFor(itemView.Item);
            glow.transform.SetAsLastSibling();
            glow.gameObject.SetActive(true);
            glow.SetVerticesDirty();
        }

        /// <summary>
        /// See-through tint over the cell's background and under the item picture: a child of the view's
        /// ColorPanel draws right after the background and before the icon.
        /// </summary>
        private static void SetFill(ItemView itemView, bool infused)
        {
            Image panel = itemView.ColorPanel;
            if (panel == null)
                return;

            Transform existing = panel.transform.Find(FillName);
            if (!infused)
            {
                if (existing != null)
                    existing.gameObject.SetActive(false);
                return;
            }

            Image fill = existing != null ? existing.GetComponent<Image>() : null;
            if (fill == null)
            {
                var go = new GameObject(FillName, typeof(RectTransform));
                go.layer = panel.gameObject.layer;
                go.AddComponent<LayoutElement>().ignoreLayout = true;

                var rt = (RectTransform)go.transform;
                rt.SetParent(panel.transform, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                fill = go.AddComponent<Image>();
                fill.raycastTarget = false;
            }

            Color color = ColorFor(itemView.Item);
            color.a = PaPHideoutModulePlugin.FillOpacity;
            fill.color = color;
            fill.gameObject.SetActive(true);
        }

        // Redrawing dirties the whole inventory canvas, so animate at ~20 fps rather than every frame
        private const float RedrawInterval = 0.05f;
        private float _nextRedraw;

        private void Update()
        {
            // scrolled out of view, nothing to see
            if (canvasRenderer.cull || Time.unscaledTime < _nextRedraw)
                return;
            _nextRedraw = Time.unscaledTime + RedrawInterval;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect r = rectTransform.rect;
            if (r.width < 2f || r.height < 2f)
                return;

            Color color = _tint;
            float perimeter = 2f * (r.width + r.height);
            float lap = Mathf.Max(0.1f, PaPHideoutModulePlugin.GlowLapSeconds);
            float head = Mathf.Repeat(Time.unscaledTime / lap, 1f) * perimeter;
            float tail = 0.3f * perimeter;
            const float thickness = 2f, glow = 5f, resting = 0.35f;

            // faint steady outline, then the moving beam over it
            DrawStrip(vh, r, head - perimeter, head, color, resting, resting, thickness, 0f, false);
            DrawStrip(vh, r, head - tail, head, color, 0f, 1f, thickness, glow, true);
        }

        // Draws the outline between two distances along the edge (clockwise from the top left
        // corner). Each point gets six vertices across the line: outer glow edge, line outer
        // edge (twice, glow and line colour), line inner edge (twice), inner glow edge.
        private void DrawStrip(VertexHelper vh, Rect r, float from, float to, Color color,
            float alphaFrom, float alphaTo, float thickness, float glow, bool hotHead)
        {
            if (to - from < 0.5f)
                return;

            // a flat colour only needs its corners, a fading one needs points all along it
            BuildSamples(r, from, to, hotHead || alphaFrom != alphaTo);

            int start = vh.currentVertCount;
            float length = to - from;

            for (int i = 0; i < _samples.Count; i++)
            {
                float d = _samples[i];
                float t = (d - from) / length;
                float alpha = Mathf.Lerp(alphaFrom, alphaTo, t * t) * color.a;

                Color line = color;
                if (hotHead)
                    line = Color.Lerp(color, Color.white, Mathf.Pow(t, 8f) * 0.65f);

                Color32 lineColor = WithAlpha(line, alpha);
                Color32 glowColor = WithAlpha(color, alpha * GlowStrength);
                Color32 clear = WithAlpha(color, 0f);

                Vector2 p = PointAt(r, d);
                Vector2 inner = Inset(r, p, thickness);

                vh.AddVert(p + Outward(r, p) * glow, clear, Vector2.zero);
                vh.AddVert(p, glowColor, Vector2.zero);
                vh.AddVert(p, lineColor, Vector2.zero);
                vh.AddVert(inner, lineColor, Vector2.zero);
                vh.AddVert(inner, glowColor, Vector2.zero);
                vh.AddVert(Inset(r, p, thickness + glow), clear, Vector2.zero);
            }

            for (int i = 0; i < _samples.Count - 1; i++)
            {
                int a = start + i * 6;
                int b = a + 6;
                Quad(vh, a, b, 0);
                Quad(vh, a, b, 2);
                Quad(vh, a, b, 4);
            }
        }

        private static void Quad(VertexHelper vh, int a, int b, int offset)
        {
            vh.AddTriangle(a + offset, a + offset + 1, b + offset + 1);
            vh.AddTriangle(b + offset + 1, b + offset, a + offset);
        }

        // Points along the strip every Step pixels, plus every corner it passes, so corners stay sharp.
        private void BuildSamples(Rect r, float from, float to, bool dense)
        {
            _samples.Clear();

            float step = dense ? Step : to - from;
            for (float d = from; d < to; d += step)
                _samples.Add(d);
            _samples.Add(to);

            float w = r.width, h = r.height, perimeter = 2f * (w + h);
            for (float lapStart = Mathf.Floor(from / perimeter) * perimeter; lapStart < to; lapStart += perimeter)
            {
                AddCorner(lapStart, from, to);
                AddCorner(lapStart + w, from, to);
                AddCorner(lapStart + w + h, from, to);
                AddCorner(lapStart + w + h + w, from, to);
            }

            _samples.Sort();
        }

        private void AddCorner(float d, float from, float to)
        {
            if (d > from && d < to)
                _samples.Add(d);
        }

        private static Vector2 PointAt(Rect r, float d)
        {
            float w = r.width, h = r.height;
            d = Mathf.Repeat(d, 2f * (w + h));

            if (d < w)
                return new Vector2(r.xMin + d, r.yMax);
            d -= w;
            if (d < h)
                return new Vector2(r.xMax, r.yMax - d);
            d -= h;
            if (d < w)
                return new Vector2(r.xMax - d, r.yMin);
            d -= w;
            return new Vector2(r.xMin, r.yMin + d);
        }

        // Pulls a point on the edge into the box. Clamping into a smaller rect gives mitred corners for free.
        private static Vector2 Inset(Rect r, Vector2 p, float by)
        {
            float x = Mathf.Min(by, r.width * 0.5f);
            float y = Mathf.Min(by, r.height * 0.5f);
            return new Vector2(
                Mathf.Clamp(p.x, r.xMin + x, r.xMax - x),
                Mathf.Clamp(p.y, r.yMin + y, r.yMax - y));
        }

        private static Vector2 Outward(Rect r, Vector2 p)
        {
            const float eps = 0.01f;
            float x = p.x <= r.xMin + eps ? -1f : p.x >= r.xMax - eps ? 1f : 0f;
            float y = p.y <= r.yMin + eps ? -1f : p.y >= r.yMax - eps ? 1f : 0f;
            return new Vector2(x, y);
        }

        private static Color32 WithAlpha(Color c, float alpha)
        {
            c.a = Mathf.Clamp01(alpha);
            return c;
        }
    }
}
