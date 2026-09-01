using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace AICompanion.Unity
{
    internal static class AiCompanionIconFactory
    {
        private const string ChatBubbleResource = "AICompanion.Unity.Assets.chat_bubble_outline.png";
        private static Sprite? _circle;
        private static Sprite? _hoverRing;
        private static Sprite? _chatBubble;

        public static readonly Color NormalBackground = new Color32(39, 37, 64, 255);
        public static readonly Color HoverBackground = new Color32(49, 46, 79, 255);
        public static readonly Color PressedBackground = new Color32(31, 29, 52, 255);
        public static readonly Color IconColor = new Color32(153, 153, 189, 255);
        public static readonly Color IconHoverColor = new Color32(188, 185, 221, 255);

        public static Sprite Circle => _circle ??= CreateCircle();
        public static Sprite HoverRing => _hoverRing ??= CreateHoverRing();
        public static Sprite ChatBubble => _chatBubble ??= LoadChatBubble();

        private static Sprite CreateCircle()
        {
            const int size = 88;
            var texture = CreateTexture("AICompanion.IconCircle", size);
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var radius = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                var alpha = radius <= 34f ? (byte)255 : radius < 35.5f ? (byte)Mathf.RoundToInt((35.5f - radius) / 1.5f * 255f) : (byte)0;
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            return FinishSprite(texture, pixels, "AICompanion.IconCircle");
        }

        private static Sprite CreateHoverRing()
        {
            const int size = 88;
            var texture = CreateTexture("AICompanion.IconHoverRing", size);
            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var radius = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                if (radius < 33.5f || radius > 37f) { pixels[y * size + x] = new Color32(0, 0, 0, 0); continue; }
                var blend = Mathf.Clamp01((x + y) / (float)(size * 2));
                var color = Color32.Lerp(new Color32(28, 207, 235, 255), new Color32(235, 47, 172, 255), blend);
                var edge = Mathf.Min(Mathf.Clamp01((radius - 33.5f) * 2f), Mathf.Clamp01((37f - radius) * 2f));
                color.a = (byte)Mathf.RoundToInt(edge * 255f);
                pixels[y * size + x] = color;
            }
            return FinishSprite(texture, pixels, "AICompanion.IconHoverRing");
        }

        private static Sprite LoadChatBubble()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ChatBubbleResource)
                ?? throw new InvalidOperationException("Embedded chat bubble icon is missing.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "AICompanion.ChatBubble",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!ImageConversion.LoadImage(texture, memory.ToArray(), true)) throw new InvalidOperationException("Chat bubble icon could not be decoded.");
            return CreateSprite(texture, "AICompanion.ChatBubble");
        }

        private static Texture2D CreateTexture(string name, int size) => new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        private static Sprite FinishSprite(Texture2D texture, Color32[] pixels, string name)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return CreateSprite(texture, name);
        }

        private static Sprite CreateSprite(Texture2D texture, string name)
        {
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
