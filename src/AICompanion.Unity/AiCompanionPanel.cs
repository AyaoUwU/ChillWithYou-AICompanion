using AICompanion.Core;
using Bulbul;
using SFB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AICompanion.Unity
{
    public sealed class AiCompanionPanel : MonoBehaviour, IConversationView
    {
        private const int VisibleMessageLimit = 120;
        private const int OverlaySortingOrder = 32000;
        private static readonly Vector2 OpenButtonSize = new Vector2(72, 72);
        private IConversationStore? _history;
        private IConversationMemoryStore? _memory;
        private IChatClient? _chat;
        private ISecretStore? _secrets;
        private Func<ApiSettings>? _settingsProvider;
        private Action<ApiSettings, string>? _saveSettings;
        private Action<string>? _diagnosticEvent;
        private UiMountPoint? _mount;
        private GameObject? _openButtonObject;
        private UnityTheme _theme = new UnityTheme();
        private RectTransform? _panel;
        private Image? _openButtonImage;
        private Image? _openButtonIcon;
        private Image? _openButtonRing;
        private Image? _panelBackground;
        private Image? _settingsBackground;
        private CanvasGroup? _panelCanvas;
        private Button? _sendButton;
        private Button? _cancelButton;
        private TMP_InputField? _messageInput;
        private TMP_Text? _status;
        private RectTransform? _messageContent;
        private ScrollRect? _scroll;
        private GameObject? _settingsPage;
        private TMP_InputField? _baseUrlInput;
        private TMP_InputField? _modelInput;
        private TMP_InputField? _keyInput;
        private TMP_InputField? _searchInput;
        private Button? _clearButton;
        private TMP_Text? _clearButtonText;
        private DateTimeOffset? _oldestVisible;
        private bool _hasOlder;
        private bool _clearArmed;
        private float _clearArmUntil;
        private ConversationState _state;
        private int _lastTickFrame = -1;
        private int _lastButtonLayoutFrame = -1000;
        private readonly List<GameObject> _rendered = new List<GameObject>();

        public event Action<string>? SendRequested;
        public event Action? CancelRequested;
        public event Action? RuntimeTickRequested;

        public static AiCompanionPanel Create(
            UiMountPoint mount,
            IConversationStore history,
            IConversationMemoryStore memory,
            IChatClient chat,
            ISecretStore secrets,
            Func<ApiSettings> settingsProvider,
            Action<ApiSettings, string> saveSettings,
            Action<string> diagnosticEvent)
        {
            var root = new GameObject("AICompanion.UI", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            AiCompanionUiOwnership.Register(root);
            root.transform.SetParent(mount.Canvas.transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = Math.Min(OverlaySortingOrder, mount.Canvas.sortingOrder + 1);
            canvas.additionalShaderChannels = mount.Canvas.additionalShaderChannels;
            var panel = root.AddComponent<AiCompanionPanel>();
            panel._mount = mount;
            panel._history = history;
            panel._memory = memory;
            panel._chat = chat;
            panel._secrets = secrets;
            panel._settingsProvider = settingsProvider;
            panel._saveSettings = saveSettings;
            panel._diagnosticEvent = diagnosticEvent;
            panel.Build();
            return panel;
        }

        public bool IsOpen => _panelCanvas != null && _panelCanvas.alpha > 0.01f;
        public void OpenForDiagnostics() => Open();

        public string GetRenderDiagnostics()
        {
            var canvas = GetComponent<Canvas>();
            var button = _openButtonImage != null ? _openButtonImage.rectTransform : null;
            return "rootActive=" + gameObject.activeInHierarchy
                + ", canvasEnabled=" + (canvas != null && canvas.enabled)
                + ", sortingOrder=" + (canvas != null ? canvas.sortingOrder : -1)
                + ", screen=" + Screen.width + "x" + Screen.height
                + ", buttonActive=" + (_openButtonImage != null && _openButtonImage.gameObject.activeInHierarchy)
                + ", buttonSize=" + (button != null ? button.rect.size.ToString() : "missing")
                + ", buttonPosition=" + (button != null ? button.anchoredPosition.ToString() : "missing")
                + ", sprite=" + (_openButtonImage != null && _openButtonImage.sprite != null)
                + ", parent=" + (transform.parent != null ? transform.parent.name : "none");
        }

        public void RefreshTheme()
        {
            _theme = UnityThemeAdapter.Discover();
            foreach (var text in GetComponentsInChildren<TMP_Text>(true)) ApplyTextTheme(text);
            if (_openButtonImage != null)
            {
                _openButtonImage.sprite = AiCompanionIconFactory.Circle;
                _openButtonImage.type = Image.Type.Simple;
                _openButtonImage.preserveAspect = true;
                _openButtonImage.material = null;
                _openButtonImage.color = AiCompanionIconFactory.NormalBackground;
                if (_openButtonIcon != null) _openButtonIcon.color = AiCompanionIconFactory.IconColor;
                var label = _openButtonImage.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.gameObject.SetActive(false);
            }
            if (_panelBackground != null)
            {
                _panelBackground.sprite = _theme.PanelSprite;
                _panelBackground.type = _theme.PanelSprite == null ? Image.Type.Simple : Image.Type.Sliced;
                _panelBackground.color = _theme.PanelColor;
            }
            if (_settingsBackground != null)
            {
                _settingsBackground.sprite = _theme.PanelSprite;
                _settingsBackground.type = _theme.PanelSprite == null ? Image.Type.Simple : Image.Type.Sliced;
                _settingsBackground.color = new Color(_theme.PanelColor.r, _theme.PanelColor.g, _theme.PanelColor.b, 0.99f);
            }
            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                if (_openButtonImage != null && button.gameObject == _openButtonImage.gameObject) continue;
                var image = button.GetComponent<Image>();
                if (image == null) continue;
                image.sprite = _theme.ButtonSprite;
                image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
                image.color = new Color(_theme.ActiveColor.r, _theme.ActiveColor.g, _theme.ActiveColor.b, 0.82f);
            }
        }

        public void Close()
        {
            if (_panelCanvas == null) return;
            _panelCanvas.alpha = 0f;
            _panelCanvas.interactable = false;
            _panelCanvas.blocksRaycasts = false;
            CancelRequested?.Invoke();
            PlayCancelSound();
        }

        public void SetState(ConversationState state)
        {
            _state = state;
            if (_sendButton != null) _sendButton.interactable = state == ConversationState.Idle;
            if (_cancelButton != null) _cancelButton.gameObject.SetActive(state != ConversationState.Idle);
            if (_status != null) _status.text = StateLabel(state);
        }

        public void AddMessage(ChatMessage message, bool animate) => RenderMessage(message, animate, false);

        public async Task TypeAssistantReplyAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            var text = RenderMessage(message, true, false);
            if (text == null) return;
            text.maxVisibleCharacters = 0;
            text.ForceMeshUpdate();
            var count = text.textInfo.characterCount;
            for (var i = 1; i <= count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                text.maxVisibleCharacters = i;
                await Task.Delay(24, cancellationToken);
            }
            text.maxVisibleCharacters = int.MaxValue;
        }

        public void ShowError(string message)
        {
            if (_status == null) return;
            _status.text = message;
            _status.color = new Color(0.94f, 0.58f, 0.54f, 1f);
        }

        private void Build()
        {
            _theme = UnityThemeAdapter.Discover();
            var root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            if (_mount == null) throw new InvalidOperationException("A resolved UI mount point is required.");
            BuildOpenButton(_mount);
            BuildPanel(root);
            SetState(ConversationState.Idle);
            CloseWithoutSound();
        }

        private void BuildOpenButton(UiMountPoint mount)
        {
            var button = CreateButton("OpenAI", mount.ToolbarParent, "AI", OpenButtonSize);
            AiCompanionUiOwnership.Register(button.gameObject);
            _openButtonObject = button.gameObject;
            var rect = (RectTransform)button.transform;
            var image = button.GetComponent<Image>();
            _openButtonImage = image;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            PositionOpenButton();
            rect.SetSiblingIndex(Math.Min(mount.SiblingIndex, mount.ToolbarParent.childCount - 1));
            image.sprite = AiCompanionIconFactory.Circle;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.material = null;
            image.color = AiCompanionIconFactory.NormalBackground;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.gameObject.SetActive(false);
            _openButtonRing = CreateIconLayer("HoverRing", rect, AiCompanionIconFactory.HoverRing, Color.clear, 72f);
            _openButtonRing.transform.SetSiblingIndex(0);
            _openButtonIcon = CreateIconLayer("ChatBubble", rect, AiCompanionIconFactory.ChatBubble, AiCompanionIconFactory.IconColor, 38f);
            button.GetComponent<UiInteractionFeedback>()?.ConfigureIcon(_openButtonIcon, _openButtonRing, _theme.Font, _theme.FontMaterial, "打开 AI 陪伴对话");
            button.onClick.AddListener(OpenFromButton);
        }

        private void OpenFromButton()
        {
            _diagnosticEvent?.Invoke("PointerClick");
            Open();
        }

        private static Image CreateIconLayer(string name, RectTransform parent, Sprite sprite, Color color, float size)
        {
            var root = CreateRect(name, parent);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(size, size);
            root.anchoredPosition = Vector2.zero;
            var image = root.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private void PositionOpenButton()
        {
            if (_mount == null || _openButtonImage == null || _mount.ToolbarParent == null || _mount.Canvas == null) return;
            var camera = _mount.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _mount.Canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_mount.ToolbarParent, _mount.TargetScreenCenter, camera, out var local))
                _openButtonImage.rectTransform.anchoredPosition = local;
        }

        private void BuildPanel(RectTransform root)
        {
            _panel = CreateRect("Panel", root);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(1, 0.5f);
            _panel.sizeDelta = new Vector2(520, 720);
            _panel.anchoredPosition = new Vector2(-32, 0);
            _panelBackground = _panel.gameObject.AddComponent<Image>();
            _panelBackground.sprite = _theme.PanelSprite;
            _panelBackground.type = _theme.PanelSprite == null ? Image.Type.Simple : Image.Type.Sliced;
            _panelBackground.color = _theme.PanelColor;
            _panelCanvas = _panel.gameObject.AddComponent<CanvasGroup>();

            var title = CreateText("Title", _panel, "和 Satone 聊聊", 28, TextAlignmentOptions.Left);
            Place(title.rectTransform, new Vector2(20, -64), new Vector2(-150, -16), new Vector2(0, 1), new Vector2(1, 1));

            var settingsButton = CreateButton("Settings", _panel, "设置", new Vector2(74, 40));
            Place((RectTransform)settingsButton.transform, new Vector2(-150, -56), new Vector2(-72, -16), new Vector2(1, 1), new Vector2(1, 1));
            settingsButton.onClick.AddListener(ToggleSettings);
            var closeButton = CreateButton("Close", _panel, "×", new Vector2(48, 40));
            Place((RectTransform)closeButton.transform, new Vector2(-64, -56), new Vector2(-16, -16), new Vector2(1, 1), new Vector2(1, 1));
            closeButton.onClick.AddListener(Close);

            BuildSearch(_panel);
            BuildMessages(_panel);
            BuildComposer(_panel);
            BuildSettings(_panel);

            _status = CreateText("Status", _panel, string.Empty, 17, TextAlignmentOptions.Left);
            _status.color = _theme.MutedTextColor;
            Place(_status.rectTransform, new Vector2(20, 14), new Vector2(-170, 42), new Vector2(0, 0), new Vector2(1, 0));

            _cancelButton = CreateButton("Cancel", _panel, "取消", new Vector2(76, 38));
            Place((RectTransform)_cancelButton.transform, new Vector2(-160, 10), new Vector2(-84, 48), new Vector2(1, 0), new Vector2(1, 0));
            _cancelButton.onClick.AddListener(() => CancelRequested?.Invoke());
        }

        private void BuildSearch(RectTransform parent)
        {
            _searchInput = CreateInput("Search", parent, "搜索完整记录…", false);
            Place((RectTransform)_searchInput.transform, new Vector2(20, -112), new Vector2(-120, -72), new Vector2(0, 1), new Vector2(1, 1));
            var searchButton = CreateButton("SearchButton", parent, "搜索", new Vector2(80, 40));
            Place((RectTransform)searchButton.transform, new Vector2(-108, -112), new Vector2(-20, -72), new Vector2(1, 1), new Vector2(1, 1));
            searchButton.onClick.AddListener(() => _ = SearchAsync());
            _searchInput.onSubmit.AddListener(ignored => { _ = SearchAsync(); });
        }

        private void BuildMessages(RectTransform parent)
        {
            var viewport = CreateRect("MessageViewport", parent);
            Place(viewport, new Vector2(20, 152), new Vector2(-20, -124), Vector2.zero, Vector2.one);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0, 0, 0, 0.14f);
            viewport.gameObject.AddComponent<RectMask2D>();

            _messageContent = CreateRect("Content", viewport);
            _messageContent.anchorMin = new Vector2(0, 1);
            _messageContent.anchorMax = new Vector2(1, 1);
            _messageContent.pivot = new Vector2(0.5f, 1);
            _messageContent.anchoredPosition = Vector2.zero;
            _messageContent.sizeDelta = Vector2.zero;
            var layout = _messageContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.padding = new RectOffset(16, 16, 14, 14);
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            var fitter = _messageContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _messageContent;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 26;

            var older = CreateButton("LoadOlder", _messageContent, "加载更早的记录", new Vector2(0, 36));
            older.onClick.AddListener(() => _ = LoadOlderAsync());
        }

        private void BuildComposer(RectTransform parent)
        {
            _messageInput = CreateInput("MessageInput", parent, "想对Satone说些什么…", true);
            Place((RectTransform)_messageInput.transform, new Vector2(20, 58), new Vector2(-112, 140), Vector2.zero, new Vector2(1, 0));
            _sendButton = CreateButton("Send", parent, "发送", new Vector2(80, 82));
            Place((RectTransform)_sendButton.transform, new Vector2(-100, 58), new Vector2(-20, 140), new Vector2(1, 0), new Vector2(1, 0));
            _sendButton.onClick.AddListener(SendCurrent);
        }

        private void BuildSettings(RectTransform parent)
        {
            _settingsPage = CreateRect("SettingsPage", parent).gameObject;
            var rect = (RectTransform)_settingsPage.transform;
            Place(rect, new Vector2(12, 56), new Vector2(-12, -68), Vector2.zero, Vector2.one);
            _settingsBackground = _settingsPage.AddComponent<Image>();
            _settingsBackground.sprite = _theme.PanelSprite;
            _settingsBackground.type = _theme.PanelSprite == null ? Image.Type.Simple : Image.Type.Sliced;
            _settingsBackground.color = new Color(_theme.PanelColor.r, _theme.PanelColor.g, _theme.PanelColor.b, 0.99f);

            var heading = CreateText("SettingsTitle", rect, "AI 连接与记录", 25, TextAlignmentOptions.Left);
            Place(heading.rectTransform, new Vector2(18, -52), new Vector2(-18, -16), new Vector2(0, 1), new Vector2(1, 1));
            var current = _settingsProvider?.Invoke() ?? new ApiSettings();
            _baseUrlInput = CreateLabeledInput(rect, "接口地址", current.BaseUrl, 68);
            _modelInput = CreateLabeledInput(rect, "模型", current.Model, 150);
            _keyInput = CreateLabeledInput(rect, "API 密钥（留空表示不修改）", string.Empty, 232);
            _keyInput.contentType = TMP_InputField.ContentType.Password;

            var save = CreateButton("SaveSettings", rect, "保存设置", new Vector2(128, 42));
            Place((RectTransform)save.transform, new Vector2(18, -364), new Vector2(146, -322), new Vector2(0, 1), new Vector2(0, 1));
            save.onClick.AddListener(SaveSettings);
            var test = CreateButton("TestConnection", rect, "测试连接", new Vector2(128, 42));
            Place((RectTransform)test.transform, new Vector2(160, -364), new Vector2(288, -322), new Vector2(0, 1), new Vector2(0, 1));
            test.onClick.AddListener(() => _ = TestConnectionAsync());

            var recordHeading = CreateText("RecordTitle", rect, "记录管理", 19, TextAlignmentOptions.Left);
            recordHeading.color = _theme.MutedTextColor;
            Place(recordHeading.rectTransform, new Vector2(18, -414), new Vector2(-18, -390), new Vector2(0, 1), new Vector2(1, 1));

            var exportTxt = CreateButton("ExportTxt", rect, "导出 TXT", new Vector2(112, 42));
            Place((RectTransform)exportTxt.transform, new Vector2(18, -462), new Vector2(130, -420), new Vector2(0, 1), new Vector2(0, 1));
            exportTxt.onClick.AddListener(() => _ = ExportAsync("txt"));
            var exportJson = CreateButton("ExportJson", rect, "导出 JSON", new Vector2(118, 42));
            Place((RectTransform)exportJson.transform, new Vector2(142, -462), new Vector2(260, -420), new Vector2(0, 1), new Vector2(0, 1));
            exportJson.onClick.AddListener(() => _ = ExportAsync("json"));
            _clearButton = CreateButton("ClearHistory", rect, "清空记录", new Vector2(118, 42));
            _clearButtonText = _clearButton.GetComponentInChildren<TMP_Text>();
            Place((RectTransform)_clearButton.transform, new Vector2(272, -462), new Vector2(390, -420), new Vector2(0, 1), new Vector2(0, 1));
            _clearButton.onClick.AddListener(() => _ = ConfirmClearAsync());

            var note = CreateText("Privacy", rect, "记录与密钥使用 Windows 当前账户加密。导出文件为明文；仅近期对话、摘要与已解锁剧情会发送给接口。", 15, TextAlignmentOptions.TopLeft);
            note.color = _theme.MutedTextColor;
            note.enableWordWrapping = true;
            Place(note.rectTransform, new Vector2(18, -528), new Vector2(-18, -478), new Vector2(0, 1), new Vector2(1, 1));

            var back = CreateButton("SettingsBack", rect, "返回聊天", new Vector2(112, 42));
            Place((RectTransform)back.transform, new Vector2(-130, 16), new Vector2(-18, 58), new Vector2(1, 0), new Vector2(1, 0));
            back.onClick.AddListener(ToggleSettings);
            _settingsPage.SetActive(false);
        }

        private TMP_InputField CreateLabeledInput(RectTransform parent, string label, string value, float top)
        {
            var title = CreateText(label + "Label", parent, label, 17, TextAlignmentOptions.Left);
            Place(title.rectTransform, new Vector2(18, -top - 24), new Vector2(-18, -top), new Vector2(0, 1), new Vector2(1, 1));
            var input = CreateInput(label + "Input", parent, string.Empty, false);
            input.text = value;
            Place((RectTransform)input.transform, new Vector2(18, -top - 72), new Vector2(-18, -top - 30), new Vector2(0, 1), new Vector2(1, 1));
            return input;
        }

        private void Open()
        {
            if (_panelCanvas == null) return;
            _diagnosticEvent?.Invoke("Open()");
            RefreshTheme();
            _panelCanvas.alpha = 1f;
            _panelCanvas.interactable = true;
            _panelCanvas.blocksRaycasts = true;
            _diagnosticEvent?.Invoke("CanvasGroup alpha=" + _panelCanvas.alpha + " blocksRaycasts=" + _panelCanvas.blocksRaycasts);
            _settingsPage?.SetActive(false);
            PlayClickSound();
            _ = LoadInitialHistoryAsync();
        }

        private void CloseWithoutSound()
        {
            if (_panelCanvas == null) return;
            _panelCanvas.alpha = 0f;
            _panelCanvas.interactable = false;
            _panelCanvas.blocksRaycasts = false;
        }

        private void SendCurrent()
        {
            if (_messageInput == null || _state != ConversationState.Idle) return;
            var text = _messageInput.text.Trim();
            if (text.Length == 0) return;
            _messageInput.text = string.Empty;
            PlayClickSound();
            SendRequested?.Invoke(text);
        }

        private async Task LoadInitialHistoryAsync()
        {
            if (_history == null) return;
            try
            {
                var page = await _history.GetPageBeforeAsync(null, 60, CancellationToken.None);
                ClearRendered();
                foreach (var message in page.Messages) RenderMessage(message, false, false);
                _hasOlder = page.HasOlder;
                _oldestVisible = page.Messages.FirstOrDefault()?.TimestampUtc;
                ScrollBottom();
            }
            catch (Exception ex) { ShowError("读取聊天记录失败：" + ex.GetType().Name); }
        }

        private async Task LoadOlderAsync()
        {
            if (_history == null || !_hasOlder) return;
            var page = await _history.GetPageBeforeAsync(_oldestVisible, 60, CancellationToken.None);
            foreach (var message in page.Messages.Reverse()) RenderMessage(message, false, true);
            _hasOlder = page.HasOlder;
            _oldestVisible = page.Messages.FirstOrDefault()?.TimestampUtc ?? _oldestVisible;
        }

        private async Task SearchAsync()
        {
            if (_history == null || _searchInput == null) return;
            var query = _searchInput.text.Trim();
            if (query.Length == 0) { await LoadInitialHistoryAsync(); return; }
            var found = await _history.SearchAsync(query, 100, CancellationToken.None);
            ClearRendered();
            foreach (var message in found) RenderMessage(message, false, false);
            _status!.text = "找到 " + found.Count + " 条记录";
        }

        private async Task ExportAsync(string format)
        {
            if (_history == null) return;
            var path = StandaloneFileBrowser.SaveFilePanel("导出 AI 聊天记录", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChillWithYou-AI-Chat-" + DateTime.Now.ToString("yyyyMMdd"), format);
            if (string.IsNullOrWhiteSpace(path)) return;
            await _history.ExportAsync(path, format, CancellationToken.None);
            _status!.text = "已导出到 " + Path.GetFileName(path);
        }

        private async Task ConfirmClearAsync()
        {
            if (_history == null) return;
            if (!_clearArmed || Time.unscaledTime > _clearArmUntil)
            {
                _clearArmed = true;
                _clearArmUntil = Time.unscaledTime + 5f;
                if (_clearButtonText != null) _clearButtonText.text = "再次点击确认";
                return;
            }
            _clearArmed = false;
            if (_clearButtonText != null) _clearButtonText.text = "清空记录";
            await _history.ClearAsync(CancellationToken.None);
            if (_memory != null) await _memory.ClearAsync(CancellationToken.None);
            ClearRendered();
            _status!.text = "聊天记录已清空（原文件保留为可恢复备份）";
        }

        private void SaveSettings()
        {
            var current = _settingsProvider?.Invoke() ?? new ApiSettings();
            current.BaseUrl = _baseUrlInput?.text.Trim() ?? current.BaseUrl;
            current.Model = _modelInput?.text.Trim() ?? current.Model;
            var key = _keyInput?.text ?? string.Empty;
            _saveSettings?.Invoke(current, key);
            if (_keyInput != null) _keyInput.text = string.Empty;
            _status!.text = "设置已保存";
            PlayClickSound();
        }

        private async Task TestConnectionAsync()
        {
            if (_chat == null) return;
            SaveSettings();
            _status!.text = "正在测试连接…";
            try { await _chat.TestConnectionAsync(CancellationToken.None); _status.text = "连接成功"; }
            catch (Exception ex) { ShowError("连接失败：" + ex.Message); }
        }

        private TMP_Text? RenderMessage(ChatMessage message, bool animate, bool prepend)
        {
            if (_messageContent == null) return null;
            var row = CreateRect("Message." + message.Id, _messageContent);
            var layout = row.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 48;
            var rowLayout = row.gameObject.AddComponent<VerticalLayoutGroup>();
            rowLayout.padding = new RectOffset(8, 8, 4, 4);
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            var text = CreateText("Text", row, (message.Role == ChatRole.User ? "你\n" : "Satone\n") + message.Content, 20, TextAlignmentOptions.TopLeft);
            text.enableWordWrapping = true;
            text.color = message.Role == ChatRole.User ? _theme.ActiveColor : _theme.TextColor;
            text.margin = new Vector4(12, 8, 12, 8);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.sizeDelta = Vector2.zero;
            var fitter = text.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var rowFitter = row.gameObject.AddComponent<ContentSizeFitter>();
            rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (prepend) row.SetSiblingIndex(1);
            _rendered.Add(row.gameObject);
            while (_rendered.Count > VisibleMessageLimit)
            {
                var oldest = _rendered[0];
                _rendered.RemoveAt(0);
                if (oldest != null) Destroy(oldest);
            }
            if (!prepend) ScrollBottom();
            return text;
        }

        private void ClearRendered()
        {
            foreach (var item in _rendered) if (item != null) Destroy(item);
            _rendered.Clear();
        }

        private void ScrollBottom()
        {
            if (_scroll == null) return;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }

        private void ToggleSettings()
        {
            if (_settingsPage == null) return;
            _settingsPage.SetActive(!_settingsPage.activeSelf);
            PlayClickSound();
        }

        private TMP_Text CreateText(string name, Transform parent, string text, float size, TextAlignmentOptions alignment)
        {
            var rect = CreateRect(name, parent);
            var component = rect.gameObject.AddComponent<TextMeshProUGUI>();
            component.text = text;
            component.fontSize = size;
            component.alignment = alignment;
            component.raycastTarget = false;
            ApplyTextTheme(component);
            return component;
        }

        private void ApplyTextTheme(TMP_Text text)
        {
            if (_theme.Font != null) text.font = _theme.Font;
            if (_theme.FontMaterial != null) text.fontSharedMaterial = _theme.FontMaterial;
            if (text.color.a < 0.1f) text.color = _theme.TextColor;
        }

        private Button CreateButton(string name, Transform parent, string label, Vector2 size)
        {
            var rect = CreateRect(name, parent);
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = _theme.ButtonSprite;
            image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = new Color(_theme.ActiveColor.r, _theme.ActiveColor.g, _theme.ActiveColor.b, 0.82f);
            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.disabledColor = _theme.DisabledColor;
            button.colors = colors;
            var text = CreateText("Label", rect, label, 18, TextAlignmentOptions.Center);
            Place(text.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            rect.gameObject.AddComponent<UiInteractionFeedback>().Initialize(button, image, text);
            return button;
        }

        private TMP_InputField CreateInput(string name, Transform parent, string placeholderText, bool multiline)
        {
            var root = CreateRect(name, parent);
            var image = root.gameObject.AddComponent<Image>();
            image.sprite = _theme.ButtonSprite;
            image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = new Color32(15, 16, 24, 232);
            var outline = root.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color32(69, 66, 94, 210);
            outline.effectDistance = new Vector2(1.4f, -1.4f);
            outline.useGraphicAlpha = true;
            var viewport = CreateRect("Text Area", root);
            Place(viewport, new Vector2(10, 6), new Vector2(-10, -6), Vector2.zero, Vector2.one);
            viewport.gameObject.AddComponent<RectMask2D>();
            var alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            var text = CreateText("Text", viewport, string.Empty, 18, alignment);
            Place(text.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            text.enableWordWrapping = multiline;
            var placeholder = CreateText("Placeholder", viewport, placeholderText, 18, alignment);
            Place(placeholder.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one);
            placeholder.color = _theme.MutedTextColor;
            var input = root.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = image;
            input.textViewport = viewport;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            input.characterLimit = multiline ? 2000 : 512;
            input.caretColor = new Color32(196, 193, 225, 255);
            input.selectionColor = new Color32(90, 85, 130, 150);
            input.transition = Selectable.Transition.None;
            root.gameObject.AddComponent<UiInputFeedback>().Initialize(input, image, outline);
            return input;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Place(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private void PlayClickSound()
        {
            var room = Resources.FindObjectsOfTypeAll<RoomGameManager>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
            ReflectionAccess.Field<SystemSeService>(room, "_systemSeService")?.PlayClick();
        }

        private void PlayCancelSound()
        {
            var room = Resources.FindObjectsOfTypeAll<RoomGameManager>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
            ReflectionAccess.Field<SystemSeService>(room, "_systemSeService")?.PlayCancel();
        }

        private static string StateLabel(ConversationState state)
        {
            switch (state)
            {
                case ConversationState.ComposingContext: return "正在回想…";
                case ConversationState.RequestingText: return "Satone正在组织语言…";
                case ConversationState.PresentingText: return "Satone正在说话…";
                case ConversationState.Cancelling: return "正在取消…";
                default: return string.Empty;
            }
        }

        public void Tick()
        {
            if (_lastTickFrame == Time.frameCount) return;
            _lastTickFrame = Time.frameCount;
            if (Time.frameCount - _lastButtonLayoutFrame >= 30)
            {
                _lastButtonLayoutFrame = Time.frameCount;
                PositionOpenButton();
            }
            if (IsOpen && _messageInput != null && _messageInput.isFocused && Input.GetKeyDown(KeyCode.Return)
                && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                SendCurrent();
            }
            if (_clearArmed && Time.unscaledTime > _clearArmUntil)
            {
                _clearArmed = false;
                if (_clearButtonText != null) _clearButtonText.text = "清空记录";
            }
            if (_status != null && (_state == ConversationState.ComposingContext || _state == ConversationState.RequestingText))
            {
                var dots = 1 + (int)(Time.unscaledTime * 2f) % 3;
                _status.text = (_state == ConversationState.ComposingContext ? "正在回想" : "Satone正在组织语言") + new string('…', dots);
            }
        }

        private void Update()
        {
            RuntimeTickRequested?.Invoke();
            Tick();
        }

        private void OnDestroy()
        {
            if (_openButtonObject != null) { AiCompanionUiOwnership.Unregister(_openButtonObject); Destroy(_openButtonObject); }
            AiCompanionUiOwnership.Unregister(gameObject);
        }
    }
}
