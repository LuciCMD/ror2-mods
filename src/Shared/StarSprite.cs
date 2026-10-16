using UnityEngine;

namespace JustBeBetter.Shared
{
    // Five-pointed stars drawn once at runtime, filled and outlined, white so they can be tinted
    // Antialiased by sampling each pixel
    internal static class StarSprite
    {
        private const int Size = 64;
        private const int Samples = 4;
        private const float Inner = 0.42f;
        private const float Stroke = 6f;

        private static Sprite? filled;
        private static Sprite? hollow;

        internal static Sprite Filled => filled ? filled! : filled = Draw(false);

        internal static Sprite Hollow => hollow ? hollow! : hollow = Draw(true);

        private static Sprite Draw(bool outline)
        {
            Vector2[] points = Points();
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    pixels[(y * Size) + x] = new Color32(255, 255, 255, (byte)(Coverage(points, x, y, outline) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }

        // Alternating outer and inner corners, point up, in pixel space
        private static Vector2[] Points()
        {
            var points = new Vector2[10];
            float centre = Size / 2f;
            float outer = (Size / 2f) - 1f;
            for (int i = 0; i < 10; i++)
            {
                float radius = i % 2 == 0 ? outer : outer * Inner;
                float angle = (Mathf.PI / 2f) + (i * Mathf.PI / 5f);
                points[i] = new Vector2(centre + (radius * Mathf.Cos(angle)), centre + (radius * Mathf.Sin(angle)));
            }

            return points;
        }

        private static float Coverage(Vector2[] points, int x, int y, bool outline)
        {
            int inside = 0;
            for (int sy = 0; sy < Samples; sy++)
            {
                for (int sx = 0; sx < Samples; sx++)
                {
                    var p = new Vector2(x + ((sx + 0.5f) / Samples), y + ((sy + 0.5f) / Samples));
                    if (Contains(points, p) && (!outline || EdgeDistance(points, p) < Stroke))
                    {
                        inside++;
                    }
                }
            }

            return inside / (float)(Samples * Samples);
        }

        private static float EdgeDistance(Vector2[] points, Vector2 p)
        {
            float nearest = float.MaxValue;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                Vector2 edge = points[i] - points[j];
                float t = Mathf.Clamp01(Vector2.Dot(p - points[j], edge) / edge.sqrMagnitude);
                nearest = Mathf.Min(nearest, Vector2.Distance(p, points[j] + (t * edge)));
            }

            return nearest;
        }

        // Even-odd ray cast
        private static bool Contains(Vector2[] points, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                if ((points[i].y > p.y) != (points[j].y > p.y)
                    && p.x < ((points[j].x - points[i].x) * (p.y - points[i].y) / (points[j].y - points[i].y)) + points[i].x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
