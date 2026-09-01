using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AICompanion.Unity
{
    internal sealed class UiInteractionFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private Button? _button;
        private RectTransform? _rect;
        private Image? _background;
        private TMP_Text? _label;
        private Image? _ring;
        private Image? _icon;
        private CanvasGroup? _tooltip;
        private Color _normalBackground;
        private Color _hoverBackground;
        private Color _pressedBackground;
        private Color _normalLabel;
        private Color _hoverLabel;
        private Color _normalIcon;
        private Color _hoverIcon;
        private bool _hovered;
        private bool _pressed;
        private float _clickPulseUntil;

        public void Initialize(Button button, Image background, TMP_Text? label)
        {
            _button = button;
            _rect = (RectTransform)button.transform;
            _background = background;
            _label = label;
            _normalBackground = background.color;
            _hoverBackground = Color.Lerp(background.color, new Color(0.55f, 0.50f, 0.78f, background.color.a), 0.22f);
            _pressedBackground = Color.Lerp(background.color, Color.black, 0.18f);
            _normalLabel = label != null ? label.color : Color.white;
            _hoverLabel = new Color32(216, 212, 242, 255);
            button.transition = Selectable.Transition.None;
        }

        public void ConfigureIcon(Image icon, Image ring, TMP_FontAsset? font, Material? fontMaterial, string tooltipText)
        {
            _normalBackground = AiCompanionIconFactory.NormalBackground;
            _hoverBackground = AiCompanionIconFactory.HoverBackground;
            _pressedBackground = AiCompanionIconFactory.PressedBackground;
            _icon = icon;
            _ring = ring;
            _normalIcon = AiCompanionIconFactory.IconColor;
            _hoverIcon = AiCompanionIconFactory.IconHoverColor;
            _background!.color = _normalBackground;
            _icon.color = _normalIcon;
            _ring.color = new Color(1f, 1f, 1f, 0f);
            _tooltip = CreateTooltip((RectTransform)transform, font, fontMaterial, tooltipText);
        }

        public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
        public void OnPointerExit(PointerEventData eventData) { _hovered = false; _pressed = false; }
        public void OnPointerDown(PointerEventData eventData) { if (_button?.interactable == true) _pressed = true; }
        public void OnPointerUp(PointerEventData eventData) => _pressed = false;
        public void OnPointerClick(PointerEventData eventData) { if (_button?.interactable == true) _clickPulseUntil = Time.unscaledTime + 0.12f; }

        private void Update()
        {
            if (_button == null || _rect == null || _background == null) return;
            var enabled = _button.interactable;
            var down = enabled && (_pressed || Time.unscaledTime < _clickPulseUntil);
            var scale = down ? 0.94f : enabled && _hovered ? 1.055f : 1f;
            var background = !enabled ? new Color(_normalBackground.r, _normalBackground.g, _normalBackground.b, _normalBackground.a * 0.48f)
                : down ? _pressedBackground : _hovered ? _hoverBackground : _normalBackground;
            var amount = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            _rect.localScale = Vector3.Lerp(_rect.localScale, Vector3.one * scale, amount);
            _background.color = Color.Lerp(_background.color, background, amount);
            if (_label != null)
            {
                var labelColor = enabled && _hovered ? _hoverLabel : _normalLabel;
                if (!enabled) labelColor.a *= 0.48f;
                _label.color = Color.Lerp(_label.color, labelColor, amount);
            }
            if (_icon != null)
            {
                var iconColor = enabled && _hovered ? _hoverIcon : _normalIcon;
                if (!enabled) iconColor.a *= 0.48f;
                _icon.color = Color.Lerp(_icon.color, iconColor, amount);
            }
            if (_ring != null)
            {
                var ringColor = Color.white;
                ringColor.a = enabled && _hovered ? 1f : 0f;
                _ring.color = Color.Lerp(_ring.color, ringColor, amount);
            }
            if (_tooltip != null)
            {
                var target = enabled && _hovered && !_pressed ? 1f : 0f;
                _tooltip.alpha = Mathf.Lerp(_tooltip.alpha, target, amount);
            }
        }

        private static CanvasGroup CreateTooltip(RectTransform parent, TMP_FontAsset? font, Material? fontMaterial, string value)
        {
            var root = new GameObject("Tooltip", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(Image), typeof(Outline));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(150f, 38f);
            rect.anchoredPosition = new Vector2(-42f, 52f);
            var canvas = root.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 40;
            var image = root.GetComponent<Image>();
            image.color = new Color32(22, 21, 35, 246);
            image.raycastTarget = false;
            var outline = root.GetComponent<Outline>();
            outline.effectColor = new Color32(133, 130, 171, 235);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.useGraphicAlpha = true;
            var group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            var textRoot = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)textRoot.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 4f);
            textRect.offsetMax = new Vector2(-10f, -4f);
            var text = textRoot.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = 16f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            if (font != null) text.font = font;
            if (fontMaterial != null) text.fontSharedMaterial = fontMaterial;
            return group;
        }
    }

    internal sealed class UiInputFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        private TMP_InputField? _input;
        private Image? _background;
        private Outline? _outline;
        private bool _hovered;
        private bool _selected;

        public void Initialize(TMP_InputField input, Image background, Outline outline)
        {
            _input = input;
            _background = background;
            _outline = outline;
        }

        public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
        public void OnPointerExit(PointerEventData eventData) => _hovered = false;
        public void OnSelect(BaseEventData eventData) => _selected = true;
        public void OnDeselect(BaseEventData eventData) => _selected = false;

        private void Update()
        {
            if (_input == null || _background == null || _outline == null) return;
            var focused = _selected || _input.isFocused;
            var targetBackground = focused ? new Color32(37, 34, 61, 246) : _hovered ? new Color32(28, 27, 45, 238) : new Color32(15, 16, 24, 232);
            var targetOutline = focused ? new Color32(153, 153, 189, 255) : _hovered ? new Color32(103, 100, 139, 235) : new Color32(69, 66, 94, 210);
            var amount = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            _background.color = Color.Lerp(_background.color, targetBackground, amount);
            _outline.effectColor = Color.Lerp(_outline.effectColor, targetOutline, amount);
        }
    }
}
