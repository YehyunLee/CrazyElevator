using UnityEngine;

namespace CrazyElevator.Shared
{
    // Small procedural badges keep passenger abilities readable without relying
    // on an external icon font or a collection of one-off image assets.
    public static class PassengerIconLibrary
    {
        public static Texture2D CreateAngryFace(Color face, Color ink)
        {
            const int size = 64;
            Color32[] pixels = Blank(size);
            Circle(pixels, size, new Vector2(32, 32), 30f, ink, true);
            Circle(pixels, size, new Vector2(32, 32), 25f, face, true);
            Circle(pixels, size, new Vector2(22, 37), 3.2f, ink, true);
            Circle(pixels, size, new Vector2(42, 37), 3.2f, ink, true);
            Line(pixels, size, new Vector2(17, 45), new Vector2(27, 41), ink, 3f);
            Line(pixels, size, new Vector2(37, 41), new Vector2(47, 45), ink, 3f);
            Line(pixels, size, new Vector2(19, 21), new Vector2(25, 26), ink, 3f);
            Line(pixels, size, new Vector2(25, 26), new Vector2(32, 28), ink, 3f);
            Line(pixels, size, new Vector2(32, 28), new Vector2(39, 26), ink, 3f);
            Line(pixels, size, new Vector2(39, 26), new Vector2(45, 21), ink, 3f);
            return Texture(pixels, size, "Passenger angry icon");
        }

        public static Texture2D CreatePassengerBadge(string kind, Color accent, Color ink, Color cream)
        {
            const int size = 96;
            Color32[] pixels = Blank(size);
            Vector2 center = new Vector2(48, 48);
            Circle(pixels, size, center, 45f, accent, true);
            Circle(pixels, size, center, 39f, ink, true);

            switch (kind)
            {
                case "MAD":
                    DrawAngryFace(pixels, size, center, accent, cream);
                    break;
                case "GROUP":
                    DrawGroup(pixels, size, center, cream);
                    break;
                case "SPEEDBOOST":
                    DrawSpeedBoost(pixels, size, center, cream);
                    break;
                case "BOSS":
                    DrawCrown(pixels, size, center, cream);
                    break;
                case "ELDERLY":
                    DrawSlow(pixels, size, center, cream);
                    break;
                case "PREGNANT":
                    DrawBaby(pixels, size, center, cream);
                    break;
                case "HANDYMAN":
                    DrawWrench(pixels, size, center, cream);
                    break;
                case "COURIER":
                    DrawParcel(pixels, size, center, cream);
                    break;
                case "INTERVIEW":
                    DrawUrgency(pixels, size, center, accent);
                    break;
                default:
                    DrawPassenger(pixels, size, center, cream);
                    break;
            }

            return Texture(pixels, size, "Passenger " + kind + " icon");
        }

        static void DrawAngryFace(Color32[] pixels, int size, Vector2 center, Color face, Color ink)
        {
            Circle(pixels, size, center, 31f, face, true);
            Circle(pixels, size, new Vector2(center.x - 11, center.y + 5), 3.5f, ink, true);
            Circle(pixels, size, new Vector2(center.x + 11, center.y + 5), 3.5f, ink, true);
            Line(pixels, size, center + new Vector2(-18, 15), center + new Vector2(-5, 9), ink, 3f);
            Line(pixels, size, center + new Vector2(5, 9), center + new Vector2(18, 15), ink, 3f);
            Line(pixels, size, center + new Vector2(-15, -14), center + new Vector2(0, -7), ink, 3f);
            Line(pixels, size, center + new Vector2(0, -7), center + new Vector2(15, -14), ink, 3f);
        }

        static void DrawSpeedBoost(Color32[] pixels, int size, Vector2 center, Color color)
        {
            Polygon(pixels, size, new[]
            {
                center + new Vector2(8, 38), center + new Vector2(-12, 5),
                center + new Vector2(2, 5), center + new Vector2(-7, -38),
                center + new Vector2(16, -1), center + new Vector2(4, -1)
            }, color);
            Line(pixels, size, center + new Vector2(-31, 25), center + new Vector2(-17, 25), color, 4f);
            Line(pixels, size, center + new Vector2(-34, 10), center + new Vector2(-22, 10), color, 4f);
        }

        static void DrawGroup(Color32[] pixels, int size, Vector2 center, Color color)
        {
            Circle(pixels, size, center + new Vector2(0, -16), 10f, color, true);
            Circle(pixels, size, center + new Vector2(-20, 0), 8f, color, true);
            Circle(pixels, size, center + new Vector2(20, 0), 8f, color, true);
            Circle(pixels, size, center + new Vector2(0, 18), 24f, color, false, 6f);
        }

        static void DrawCrown(Color32[] pixels, int size, Vector2 center, Color color)
        {
            Polygon(pixels, size, new[]
            {
                center + new Vector2(-29, -20), center + new Vector2(-21, 20),
                center + new Vector2(-8, 7), center + new Vector2(0, 25),
                center + new Vector2(10, 7), center + new Vector2(24, 20),
                center + new Vector2(29, -20)
            }, color);
            Rect(pixels, size, new Rect(center.x - 29, center.y - 25, 58, 8), color);
        }

        static void DrawSlow(Color32[] pixels, int size, Vector2 center, Color color)
        {
            Circle(pixels, size, center + new Vector2(-8, 3), 19f, color, false, 5f);
            Line(pixels, size, center + new Vector2(-27, -16), center + new Vector2(25, -16), color, 5f);
            Line(pixels, size, center + new Vector2(25, -16), center + new Vector2(33, -8), color, 5f);
            Line(pixels, size, center + new Vector2(25, -16), center + new Vector2(33, -24), color, 5f);
        }

        static void DrawBaby(Color32[] pixels, int size, Vector2 center, Color color)
        {
            Circle(pixels, size, center + new Vector2(0, 12), 22f, color, false, 5f);
            Circle(pixels, size, center + new Vector2(-8, 14), 3f, color, true);
            Circle(pixels, size, center + new Vector2(8, 14), 3f, color, true);
            Circle(pixels, size, center + new Vector2(0, 3), 3f, color, false, 3f);
            Line(pixels, size, center + new Vector2(-16, -18), center + new Vector2(16, -18), color, 5f);
            Line(pixels, size, center + new Vector2(-16, -18), center + new Vector2(0, -29), color, 5f);
            Line(pixels, size, center + new Vector2(0, -29), center + new Vector2(16, -18), color, 5f);
        }

        static void DrawWrench(Color32[] pixels, int size, Vector2 center, Color color)
        {
            Line(pixels, size, center + new Vector2(-23, -22), center + new Vector2(22, 23), color, 8f);
            Circle(pixels, size, center + new Vector2(-25, -25), 11f, color, false, 5f);
            Circle(pixels, size, center + new Vector2(24, 24), 9f, color, true);
            Circle(pixels, size, center + new Vector2(24, 24), 4f, Color.clear, true);
        }

        static void DrawParcel(Color32[] pixels, int size, Vector2 center, Color color)
        {
            Rect(pixels, size, new Rect(center.x - 25, center.y - 22, 50, 44), color, false, 5f);
            Line(pixels, size, center + new Vector2(0, -22), center + new Vector2(0, 22), color, 4f);
            Line(pixels, size, center + new Vector2(-25, 2), center + new Vector2(25, 2), color, 4f);
        }

        static void DrawUrgency(Color32[] pixels, int size, Vector2 center, Color color)
        {
            Rect(pixels, size, new Rect(center.x - 7, center.y - 24, 14, 39), color);
            Circle(pixels, size, center + new Vector2(0, -34), 6f, color, true);
        }

        static void DrawPassenger(Color32[] pixels, int size, Vector2 center, Color color)
        {
            Circle(pixels, size, center + new Vector2(0, 16), 13f, color, false, 5f);
            Line(pixels, size, center + new Vector2(-20, -25), center + new Vector2(20, -25), color, 6f);
            Line(pixels, size, center + new Vector2(-20, -25), center + new Vector2(-11, -7), color, 6f);
            Line(pixels, size, center + new Vector2(20, -25), center + new Vector2(11, -7), color, 6f);
        }

        static Color32[] Blank(int size)
        {
            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 0);
            return pixels;
        }

        static Texture2D Texture(Color32[] pixels, int size, string name)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static void Circle(Color32[] pixels, int size, Vector2 center, float radius,
            Color color, bool filled, float thickness = 1f)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius - 1));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(center.x + radius + 1));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius - 1));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(center.y + radius + 1));
            float inner = radius - thickness;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), center);
                bool hit = filled ? distance <= radius : distance <= radius && distance >= inner;
                if (hit) pixels[y * size + x] = color;
            }
        }

        static void Rect(Color32[] pixels, int size, Rect rect, Color color,
            bool filled = true, float thickness = 1f)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(rect.xMin));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(rect.xMax));
            int minY = Mathf.Max(0, Mathf.FloorToInt(rect.yMin));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(rect.yMax));
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                bool hit = filled || x < minX + thickness || x > maxX - thickness
                    || y < minY + thickness || y > maxY - thickness;
                if (hit) pixels[y * size + x] = color;
            }
        }

        static void Line(Color32[] pixels, int size, Vector2 from, Vector2 to,
            Color color, float thickness)
        {
            float length = Vector2.Distance(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length * 2f));
            for (int i = 0; i <= steps; i++)
                Circle(pixels, size, Vector2.Lerp(from, to, i / (float)steps), thickness * .5f, color, true);
        }

        static void Polygon(Color32[] pixels, int size, Vector2[] points, Color color)
        {
            if (points == null || points.Length < 3) return;
            float minY = points[0].y, maxY = points[0].y;
            foreach (Vector2 point in points)
            {
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
            for (int y = Mathf.Max(0, Mathf.FloorToInt(minY)); y <= Mathf.Min(size - 1, Mathf.CeilToInt(maxY)); y++)
            {
                float scanY = y + .5f;
                var intersections = new System.Collections.Generic.List<float>();
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[(i + 1) % points.Length];
                    if ((a.y > scanY) == (b.y > scanY) || Mathf.Approximately(a.y, b.y)) continue;
                    intersections.Add(a.x + (scanY - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                intersections.Sort();
                for (int i = 0; i + 1 < intersections.Count; i += 2)
                {
                    int left = Mathf.Max(0, Mathf.FloorToInt(intersections[i]));
                    int right = Mathf.Min(size - 1, Mathf.CeilToInt(intersections[i + 1]));
                    for (int x = left; x <= right; x++) pixels[y * size + x] = color;
                }
            }
        }
    }
}
