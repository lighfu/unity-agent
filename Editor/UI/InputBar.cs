using System;
using AjisaiFlow.MD3SDK.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;


namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>
    /// チャット入力バー: テキストフィールド + 送信/停止/添付ボタン + プロバイダー/モデル Chip。
    /// </summary>
    internal class InputBar : VisualElement
    {
        readonly MD3Theme _theme;
        MD3TextField _textField;
        MD3IconButton _sendBtn;
        MD3IconButton _stopBtn;
        MD3IconButton _attachBtn;
        MD3Chip _providerChip;
        MD3Chip _modelChip;
        VisualElement _attachPreview;
        Image _attachImage;
        bool _isProcessing;

        public Action OnSendClicked;
        public Action OnStopClicked;
        public Action OnAttachClicked;
        public Action OnProviderChipClicked;
        public Action OnModelChipClicked;
        public Action OnSaveLogClicked;
        public Func<string> GetUserQuery;
        public Action<string> SetUserQuery;

        public InputBar(MD3Theme theme)
        {
            _theme = theme;

            style.flexShrink = 0;
            style.paddingLeft = 12;
            style.paddingRight = 12;
            style.paddingTop = 8;
            style.paddingBottom = 8;
            style.backgroundColor = theme.SurfaceContainerLow;
            style.borderTopWidth = 1;
            style.borderTopColor = theme.OutlineVariant;

            BuildUI();
        }

        void BuildUI()
        {
            // ── Chip row (プロバイダー + モデル) ──
            var chipRow = new MD3Row(gap: 6f);
            chipRow.style.marginBottom = 6;

            _providerChip = new MD3Chip("Provider", false);
            _providerChip.clicked += () => OnProviderChipClicked?.Invoke();
            chipRow.Add(_providerChip);

            _modelChip = new MD3Chip("Model", false);
            _modelChip.clicked += () => OnModelChipClicked?.Invoke();
            chipRow.Add(_modelChip);

            chipRow.Add(new MD3Spacer());

            var saveLogBtn = new MD3Button(M("ログを保存"), MD3ButtonStyle.Text, size: MD3ButtonSize.Small);
            saveLogBtn.clicked += () => OnSaveLogClicked?.Invoke();
            chipRow.Add(saveLogBtn);

            Add(chipRow);

            // ── 添付プレビュー ──
            _attachPreview = new MD3Row(gap: 8f);
            _attachPreview.style.alignItems = Align.Center;
            _attachPreview.style.marginBottom = 4;
            _attachPreview.style.display = DisplayStyle.None;

            _attachImage = new Image();
            _attachImage.style.width = 60;
            _attachImage.style.height = 45;
            _attachImage.style.borderTopLeftRadius = 6;
            _attachImage.style.borderTopRightRadius = 6;
            _attachImage.style.borderBottomLeftRadius = 6;
            _attachImage.style.borderBottomRightRadius = 6;
            _attachImage.scaleMode = ScaleMode.ScaleToFit;
            _attachPreview.Add(_attachImage);

            var removeAttachBtn = new MD3IconButton(MD3Icon.Close, MD3IconButtonStyle.Standard, MD3IconButtonSize.Small);
            removeAttachBtn.clicked += () => ClearAttachmentPreview();
            _attachPreview.Add(removeAttachBtn);

            Add(_attachPreview);

            // ── Input row ──
            var inputRow = new MD3Row(gap: 6f);
            inputRow.style.alignItems = Align.Center;

            // Attach button
            _attachBtn = new MD3IconButton("\ue226", MD3IconButtonStyle.Standard, MD3IconButtonSize.Small);
            _attachBtn.style.flexShrink = 0;
            _attachBtn.clicked += () => OnAttachClicked?.Invoke();
            inputRow.Add(_attachBtn);

            // Text field (MD3TextField Plain style)
            _textField = new MD3TextField("", MD3TextFieldStyle.Plain, multiline: true,
                placeholder: M("メッセージを入力..."));
            _textField.CustomBackgroundColor = _theme.SurfaceContainerHigh;
            _textField.BorderRadius = 18f;
            _textField.style.flexGrow = 1;
            _textField.style.flexShrink = 1;
            _textField.style.minHeight = 36;
            _textField.style.maxHeight = 120;

            // Key handler: Enter to send, Shift+Enter to newline
            _textField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return && !evt.shiftKey)
                {
                    evt.StopPropagation();
                    evt.PreventDefault();
                    TrySend();
                }
            });

            _textField.changed += text => SetUserQuery?.Invoke(text);

            inputRow.Add(_textField);

            // Send button
            _sendBtn = new MD3IconButton(MD3Icon.Send, MD3IconButtonStyle.Filled, MD3IconButtonSize.Small);
            _sendBtn.style.flexShrink = 0;
            _sendBtn.clicked += () => TrySend();
            inputRow.Add(_sendBtn);

            // Stop button (hidden by default)
            _stopBtn = new MD3IconButton("\ue047", MD3IconButtonStyle.Tonal, MD3IconButtonSize.Small);
            _stopBtn.style.flexShrink = 0;
            _stopBtn.style.display = DisplayStyle.None;
            _stopBtn.clicked += () => OnStopClicked?.Invoke();
            inputRow.Add(_stopBtn);

            Add(inputRow);

            // Drag and drop support
            RegisterCallback<DragEnterEvent>(evt => DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
            RegisterCallback<DragUpdatedEvent>(evt => DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
            RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                string current = _textField.Value ?? "";
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    string assetPath = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(assetPath))
                        current += $" (Asset: {assetPath})";
                    else if (obj is GameObject go)
                        current += $" (GameObject: \"{go.name}\")";
                    else if (obj != null)
                        current += $" (Object: {obj.name})";
                }
                _textField.Value = current;
            });
        }

        void TrySend()
        {
            if (_isProcessing) return;
            OnSendClicked?.Invoke();
        }

        public void SetProcessing(bool processing)
        {
            if (_isProcessing == processing) return;
            _isProcessing = processing;

            _sendBtn.style.display = processing ? DisplayStyle.None : DisplayStyle.Flex;
            _stopBtn.style.display = processing ? DisplayStyle.Flex : DisplayStyle.None;
            _textField.SetEnabled(!processing);
            _attachBtn.SetEnabled(!processing);
        }

        public void UpdateProviderName(string name)
        {
            _providerChip.Text = name ?? "Provider";
        }

        public void UpdateModelName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                _modelChip.style.display = DisplayStyle.None;
            }
            else
            {
                _modelChip.style.display = DisplayStyle.Flex;
                _modelChip.Text = name;
            }
        }

        public string GetText() => _textField.Value;
        public void SetText(string text) => _textField.Value = text ?? "";
        public void ClearText() => _textField.Value = "";
        public new void Focus() => _textField.Focus();

        public void ShowAttachmentPreview(Texture2D preview)
        {
            if (preview == null)
            {
                ClearAttachmentPreview();
                return;
            }
            _attachImage.image = preview;
            _attachPreview.style.display = DisplayStyle.Flex;
        }

        public void ClearAttachmentPreview()
        {
            _attachImage.image = null;
            _attachPreview.style.display = DisplayStyle.None;
        }
    }
}
