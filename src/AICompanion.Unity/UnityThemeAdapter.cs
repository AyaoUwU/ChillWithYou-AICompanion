using Bulbul;
using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AICompanion.Unity
{
    public sealed class UnityTheme
    {
        public TMP_FontAsset? Font { get; set; }
        public Material? FontMaterial { get; set; }
        public Sprite? TalkButtonSprite { get; set; }
        public Sprite? SideButtonSprite { get; set; }
        public Color SideButtonColor { get; set; } = new Color(0.18f, 0.16f, 0.31f, 0.98f);
        public Sprite? PanelSprite { get; set; }
        public Sprite? ButtonSprite { get; set; }
        public Color PanelColor { get; set; } = new Color(0.10f, 0.12f, 0.14f, 0.94f);
        public Color TextColor { get; set; } = new Color(0.94f, 0.92f, 0.88f, 1f);
        public Color MutedTextColor { get; set; } = new Color(0.68f, 0.70f, 0.72f, 1f);
        public Color ActiveColor { get; set; } = new Color(0.78f, 0.67f, 0.55f, 1f);
        public Color DisabledColor { get; set; } = new Color(0.35f, 0.36f, 0.38f, 0.65f);
    }

    public static class UnityThemeAdapter
    {
        public static UnityTheme Discover()
        {
            var theme = new UnityTheme();
            var sampleText = Resources.FindObjectsOfTypeAll<TMP_Text>()
                .FirstOrDefault(x => x != null && x.font != null && x.gameObject.scene.IsValid());
            if (sampleText != null)
            {
                theme.Font = sampleText.font;
                theme.FontMaterial = sampleText.fontSharedMaterial;
                theme.TextColor = sampleText.color.a > 0.2f ? sampleText.color : theme.TextColor;
            }

            var wantTalk = Resources.FindObjectsOfTypeAll<WantTalkUI>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
            theme.TalkButtonSprite = ReflectionAccess.Field(wantTalk, "_wantTalkNormalSprite") as Sprite;

            var sideButton = Resources.FindObjectsOfTypeAll<Button>()
                .Where(x => x != null && x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy)
                .Select(x => new { Button = x, Image = x.GetComponent<Image>(), Rect = x.transform as RectTransform })
                .Where(x => x.Image != null && x.Image.sprite != null && x.Rect != null)
                .Where(x =>
                {
                    var size = x.Rect!.rect.size;
                    return size.x >= 48f && size.x <= 100f && size.y >= 48f && size.y <= 100f
                        && Math.Abs(size.x - size.y) <= 18f;
                })
                .Select(x =>
                {
                    var canvas = x.Button.GetComponentInParent<Canvas>();
                    var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    var screen = RectTransformUtility.WorldToScreenPoint(camera, x.Rect!.position);
                    return new { x.Image, Screen = screen };
                })
                .Where(x => Screen.width > 0 && x.Screen.x >= Screen.width * 0.84f && x.Screen.y <= Screen.height * 0.72f)
                .OrderBy(x => Math.Abs(x.Screen.x - Screen.width * 0.965f))
                .ThenBy(x => Math.Abs(x.Screen.y - Screen.height * 0.25f))
                .FirstOrDefault();
            if (sideButton != null)
            {
                theme.SideButtonSprite = sideButton.Image!.sprite;
                theme.SideButtonColor = sideButton.Image.color;
            }

            var settings = Resources.FindObjectsOfTypeAll<SettingUI>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
            if (settings != null)
            {
                var active = ReflectionAccess.Field(settings, "ActiveColor");
                var deactive = ReflectionAccess.Field(settings, "DeactiveColor");
                if (active is Color32 activeColor) theme.ActiveColor = activeColor;
                if (deactive is Color32 deactiveColor) theme.DisabledColor = deactiveColor;
                var candidates = settings.GetComponentsInChildren<Image>(true).Where(x => x.sprite != null).ToArray();
                var panelCandidate = candidates.OrderByDescending(x => Math.Abs(x.rectTransform.sizeDelta.x * x.rectTransform.sizeDelta.y)).FirstOrDefault();
                theme.PanelSprite = panelCandidate?.sprite;
                if (panelCandidate != null && panelCandidate.color.a > 0.2f) theme.PanelColor = panelCandidate.color;
                theme.ButtonSprite = candidates
                    .Where(x => Math.Abs(x.rectTransform.rect.width) >= 60f && Math.Abs(x.rectTransform.rect.height) >= 20f)
                    .Where(x =>
                    {
                        var height = Math.Max(1f, Math.Abs(x.rectTransform.rect.height));
                        var ratio = Math.Abs(x.rectTransform.rect.width) / height;
                        return ratio >= 1.4f && ratio <= 6f;
                    })
                    .OrderBy(x => Math.Abs(x.rectTransform.rect.width * x.rectTransform.rect.height))
                    .FirstOrDefault()?.sprite;
            }

            if (theme.ButtonSprite == null) theme.ButtonSprite = theme.PanelSprite;
            return theme;
        }
    }
}
