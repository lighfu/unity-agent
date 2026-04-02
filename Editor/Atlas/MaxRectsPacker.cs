using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace AjisaiFlow.UnityAgent.Editor.Atlas
{
    /// <summary>
    /// MaxRects Best Short Side Fit bin packing for atlas layout preview.
    /// Actual atlas generation is handled by AAO at build time.
    /// </summary>
    internal static class MaxRectsPacker
    {
        internal static AtlasLayout Pack(
            List<(int materialIndex, int width, int height)> items,
            int padding, int maxSize)
        {
            if (items == null || items.Count == 0)
                return new AtlasLayout { AtlasWidth = 0, AtlasHeight = 0, Efficiency = 0f };

            // Sort by area descending
            var sorted = items.OrderByDescending(i => i.width * i.height).ToList();

            // Try progressively larger atlas sizes (powers of 2)
            for (int size = NextPowerOfTwo(64); size <= maxSize; size *= 2)
            {
                var result = TryPack(sorted, padding, size, size);
                if (result != null)
                    return result;
            }

            // If maxSize is not power-of-two, try it directly
            var lastAttempt = TryPack(sorted, padding, maxSize, maxSize);
            if (lastAttempt != null)
                return lastAttempt;

            // Could not fit — return best-effort with maxSize
            return new AtlasLayout
            {
                AtlasWidth = maxSize,
                AtlasHeight = maxSize,
                Rects = new List<AtlasRect>(),
                Efficiency = 0f
            };
        }

        private static AtlasLayout TryPack(
            List<(int materialIndex, int width, int height)> items,
            int padding, int atlasW, int atlasH)
        {
            var freeRects = new List<Rect> { new Rect(0, 0, atlasW, atlasH) };
            var placed = new List<AtlasRect>();
            long usedArea = 0;

            foreach (var (matIdx, w, h) in items)
            {
                int pw = w + padding * 2;
                int ph = h + padding * 2;

                int bestIdx = -1;
                float bestShortSide = float.MaxValue;
                float bestLongSide = float.MaxValue;

                for (int i = 0; i < freeRects.Count; i++)
                {
                    var r = freeRects[i];
                    if (pw <= r.width && ph <= r.height)
                    {
                        float shortSide = Mathf.Min(r.width - pw, r.height - ph);
                        float longSide = Mathf.Max(r.width - pw, r.height - ph);
                        if (shortSide < bestShortSide ||
                            (Mathf.Approximately(shortSide, bestShortSide) && longSide < bestLongSide))
                        {
                            bestIdx = i;
                            bestShortSide = shortSide;
                            bestLongSide = longSide;
                        }
                    }
                }

                if (bestIdx < 0)
                    return null; // Does not fit

                var bestRect = freeRects[bestIdx];
                int px = (int)bestRect.x + padding;
                int py = (int)bestRect.y + padding;

                placed.Add(new AtlasRect
                {
                    MaterialIndex = matIdx,
                    PackedX = px,
                    PackedY = py,
                    PackedWidth = w,
                    PackedHeight = h
                });

                usedArea += (long)w * h;

                // Split free rects
                var usedRect = new Rect(bestRect.x, bestRect.y, pw, ph);
                SplitFreeRects(freeRects, usedRect);
                PruneFreeRects(freeRects);
            }

            float efficiency = (float)usedArea / ((long)atlasW * atlasH);

            return new AtlasLayout
            {
                AtlasWidth = atlasW,
                AtlasHeight = atlasH,
                Rects = placed,
                Efficiency = efficiency
            };
        }

        private static void SplitFreeRects(List<Rect> freeRects, Rect used)
        {
            int count = freeRects.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                var free = freeRects[i];
                if (!Overlaps(free, used)) continue;

                freeRects.RemoveAt(i);

                // Left
                if (used.x > free.x)
                    freeRects.Add(new Rect(free.x, free.y, used.x - free.x, free.height));

                // Right
                if (used.xMax < free.xMax)
                    freeRects.Add(new Rect(used.xMax, free.y, free.xMax - used.xMax, free.height));

                // Bottom
                if (used.y > free.y)
                    freeRects.Add(new Rect(free.x, free.y, free.width, used.y - free.y));

                // Top
                if (used.yMax < free.yMax)
                    freeRects.Add(new Rect(free.x, used.yMax, free.width, free.yMax - used.yMax));
            }
        }

        private static void PruneFreeRects(List<Rect> freeRects)
        {
            for (int i = freeRects.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < freeRects.Count; j++)
                {
                    if (i == j || i >= freeRects.Count) continue;
                    if (Contains(freeRects[j], freeRects[i]))
                    {
                        freeRects.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static bool Overlaps(Rect a, Rect b)
        {
            return a.x < b.xMax && a.xMax > b.x && a.y < b.yMax && a.yMax > b.y;
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.x >= outer.x && inner.y >= outer.y &&
                   inner.xMax <= outer.xMax && inner.yMax <= outer.yMax;
        }

        private static int NextPowerOfTwo(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return v + 1;
        }
    }
}
