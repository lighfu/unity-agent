using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityEditor;
using AjisaiFlow.MD3SDK.Editor;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class MochiFitterCatalogWindow : EditorWindow
    {
        // ── Constants ───────────────────────────────────────────────
        private static readonly string CatalogPath =
            Path.Combine("Packages", "com.ajisaiflow.unityagent", "Editor", "Data", "mochifitter_catalog.json");
        private static readonly string ThumbCacheDir =
            Path.Combine("Library", "UnityAgent", "MochiFitterThumbs");

        // ── Data ────────────────────────────────────────────────────
        private MochiFitterCatalogRoot _catalog;
        private List<MochiFitterProfileEntry> _filtered;

        // ── UI state ────────────────────────────────────────────────
        private MD3Theme _theme;
        private MD3VirtualList<MochiFitterProfileEntry> _virtualList;
        private ScrollView _gridScroll;
        private VisualElement _gridContainer;
        private VisualElement _listHost;   // parent of _virtualList
        private VisualElement _gridHost;   // parent of _gridScroll
        private ScrollView _detailScroll;
        private Label _statusLabel;
        private MD3TextField _searchField;
        private int _filterConvIdx;     // 0=All, 1=both, 2=forward, 3=reverse, 4=unknown
        private int _filterPriceIdx;    // 0=All, 1=free, 2=paid
        private int _selectedIndex = -1;
        private MochiFitterProfileEntry _selectedEntry;
        private bool _isGridMode = true; // default: grid

        private static readonly string[] ConvFilterValues = { "", "both", "forward", "reverse", "unknown" };
        private const float GridCardWidth = 140f;
        private const float GridCardHeight = 180f;
        private const float GridGap = 8f;

        // ── Thumbnail ───────────────────────────────────────────────
        private static readonly Dictionary<string, Texture2D> _thumbCache = new Dictionary<string, Texture2D>();
        private static readonly HashSet<string> _thumbLoading = new HashSet<string>();
        private static readonly HashSet<string> _thumbFailed = new HashSet<string>();
        private const int MaxConcurrentThumbLoads = 4;
        private static int _activeThumbLoads;

        // Priority queue: visible items get loaded first.
        // Items are added at front (newest bind = most visible) and consumed from front.
        private readonly LinkedList<ThumbRequest> _thumbQueue = new LinkedList<ThumbRequest>();
        private bool _thumbPumpRegistered;

        private struct ThumbRequest
        {
            public MochiFitterProfileEntry entry;
            public Action onComplete;
        }

        // Batch fetch state
        private bool _isFetchingUrls;
        private int _fetchIdx;
        private UnityWebRequest _fetchReq;

        // ── Menu ────────────────────────────────────────────────────
        [MenuItem("Window/紫陽花広場/MochiFitter Catalog")]
        public static void Open()
        {
            if (UpdateChecker.IsBlocked)
            {
                EditorUtility.DisplayDialog(M("バージョン期限切れ"),
                    UpdateChecker.IsExpired
                        ? M("このバージョンは期限切れです。最新バージョンを BOOTH からダウンロードしてください。")
                        : M("ライセンス認証に失敗しました。インターネット接続を確認し、Unity を再起動してください。"),
                    "OK");
                return;
            }
            var window = GetWindow<MochiFitterCatalogWindow>();
            window.titleContent = new GUIContent(M("もちふぃった～ カタログ"));
            window.minSize = new Vector2(700, 450);
            window.Show();
        }

        // ── Lifecycle ───────────────────────────────────────────────
        private void CreateGUI()
        {
            rootVisualElement.Clear();

            _theme = ResolveTheme();
            var themeSheet = MD3Theme.LoadThemeStyleSheet();
            var compSheet = MD3Theme.LoadComponentsStyleSheet();
            if (themeSheet != null && !rootVisualElement.styleSheets.Contains(themeSheet))
                rootVisualElement.styleSheets.Add(themeSheet);
            if (compSheet != null && !rootVisualElement.styleSheets.Contains(compSheet))
                rootVisualElement.styleSheets.Add(compSheet);
            _theme.ApplyTo(rootVisualElement);

            rootVisualElement.style.flexGrow = 1;

            _catalog = LoadCatalogStatic();
            ApplyFilter();
            BuildLayout();
        }

        private void OnDisable()
        {
            StopFetchUrls();
            StopThumbPump();
        }

        // ── Theme ───────────────────────────────────────────────────
        private static MD3Theme ResolveTheme()
        {
            switch (AgentSettings.ThemeMode)
            {
                case 1: return MD3Theme.Dark();
                case 2: return MD3Theme.Light();
                case 3: return AgentSettings.BuildCustomTheme();
                default: return MD3Theme.Auto();
            }
        }

        // ── Layout ──────────────────────────────────────────────────
        private void BuildLayout()
        {
            var root = rootVisualElement;

            // ─── Header ─────────────────────────────────────────
            var header = new VisualElement();
            header.style.paddingLeft = 12;
            header.style.paddingRight = 12;
            header.style.paddingTop = 10;
            header.style.paddingBottom = 8;
            header.style.flexShrink = 0;

            // Title row
            var titleRow = new MD3Row(8);
            var titleLabel = new Label(M("もちふぃった～ カタログ"));
            titleLabel.style.fontSize = 16;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = _theme.OnSurface;
            titleLabel.style.flexGrow = 1;
            titleRow.Add(titleLabel);

            var thumbBtn = new MD3Button(M("サムネイル取得"), MD3ButtonStyle.Tonal);
            thumbBtn.clicked += FetchThumbnailUrls;
            titleRow.Add(thumbBtn);
            header.Add(titleRow);

            // Search + Filters row
            var filterRow = new MD3Row(8);
            filterRow.style.marginTop = 8;

            _searchField = new MD3TextField("", MD3TextFieldStyle.Outlined, placeholder: M("検索..."));
            _searchField.style.flexGrow = 1;
            _searchField.style.maxWidth = 260;
            _searchField.changed += _ => { ApplyFilter(); RefreshView(); };
            filterRow.Add(_searchField);

            var convLabels = new[] { M("全タイプ"), M("両変換"), M("順変換"), M("逆変換"), M("不明") };
            var convSeg = new MD3SegmentedButton(convLabels, 0);
            convSeg.changed += idx => { _filterConvIdx = idx; ApplyFilter(); RefreshView(); };
            filterRow.Add(convSeg);

            var priceLabels = new[] { M("全価格"), M("無料"), M("有料") };
            var priceSeg = new MD3SegmentedButton(priceLabels, 0);
            priceSeg.changed += idx => { _filterPriceIdx = idx; ApplyFilter(); RefreshView(); };
            filterRow.Add(priceSeg);

            // View mode toggle (grid / list)
            var viewLabels = new[] { M("グリッド"), M("リスト") };
            var viewSeg = new MD3SegmentedButton(viewLabels, _isGridMode ? 0 : 1);
            viewSeg.changed += idx => { _isGridMode = idx == 0; SwitchViewMode(); };
            filterRow.Add(viewSeg);

            header.Add(filterRow);
            root.Add(header);

            // Divider
            AddDivider(root);

            // ─── Main content (two-column) ──────────────────────
            var main = new VisualElement();
            main.style.flexGrow = 1;
            main.style.flexDirection = FlexDirection.Row;
            main.style.overflow = Overflow.Hidden;

            // Left: list view host
            _listHost = new VisualElement();
            _listHost.style.flexGrow = 1;
            _listHost.style.minWidth = 340;
            _listHost.style.display = _isGridMode ? DisplayStyle.None : DisplayStyle.Flex;

            _virtualList = new MD3VirtualList<MochiFitterProfileEntry>(itemHeight: 56f, overscan: 3);
            _virtualList.style.flexGrow = 1;
            _virtualList.SetData(_filtered, BindListItem, MakeListItem);
            _virtualList.itemClicked += OnItemClicked;
            _listHost.Add(_virtualList);
            main.Add(_listHost);

            // Left: grid view host
            _gridHost = new VisualElement();
            _gridHost.style.flexGrow = 1;
            _gridHost.style.minWidth = 340;
            _gridHost.style.display = _isGridMode ? DisplayStyle.Flex : DisplayStyle.None;

            _gridScroll = new ScrollView(ScrollViewMode.Vertical);
            _gridScroll.style.flexGrow = 1;

            _gridContainer = new VisualElement();
            _gridContainer.style.flexDirection = FlexDirection.Row;
            _gridContainer.style.flexWrap = Wrap.Wrap;
            _gridContainer.style.paddingLeft = GridGap;
            _gridContainer.style.paddingRight = 0;
            _gridContainer.style.paddingTop = GridGap;
            _gridScroll.Add(_gridContainer);

            _gridHost.Add(_gridScroll);
            main.Add(_gridHost);

            if (_isGridMode) RebuildGrid();

            // Vertical divider
            var vdivider = new VisualElement();
            vdivider.style.width = 1;
            vdivider.style.backgroundColor = _theme.OutlineVariant;
            main.Add(vdivider);

            // Right: detail panel
            _detailScroll = new ScrollView(ScrollViewMode.Vertical);
            _detailScroll.style.width = 280;
            _detailScroll.style.flexShrink = 0;
            _detailScroll.style.paddingLeft = 12;
            _detailScroll.style.paddingRight = 12;
            _detailScroll.style.paddingTop = 12;
            _detailScroll.style.paddingBottom = 12;
            _detailScroll.style.backgroundColor = _theme.SurfaceContainerLow;
            ShowEmptyDetail();
            main.Add(_detailScroll);

            root.Add(main);

            // ─── Footer ─────────────────────────────────────────
            AddDivider(root);
            var footer = new MD3Row(8);
            footer.style.paddingLeft = 12;
            footer.style.paddingRight = 12;
            footer.style.paddingTop = 6;
            footer.style.paddingBottom = 6;
            footer.style.flexShrink = 0;

            _statusLabel = new Label();
            _statusLabel.style.color = _theme.OnSurfaceVariant;
            _statusLabel.style.fontSize = 11;
            footer.Add(_statusLabel);
            root.Add(footer);

            UpdateStatus();
        }

        private void AddDivider(VisualElement parent)
        {
            var d = new VisualElement();
            d.style.height = 1;
            d.style.backgroundColor = _theme.OutlineVariant;
            d.style.flexShrink = 0;
            parent.Add(d);
        }

        // ── Virtual List Item ───────────────────────────────────────
        private VisualElement MakeListItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.paddingTop = 4;
            row.style.paddingBottom = 4;
            row.style.height = 56;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = _theme.OutlineVariant;

            // Thumbnail
            var thumb = new VisualElement();
            thumb.name = "thumb";
            thumb.style.width = 44;
            thumb.style.height = 44;
            thumb.style.flexShrink = 0;
            thumb.style.borderTopLeftRadius = 6;
            thumb.style.borderTopRightRadius = 6;
            thumb.style.borderBottomLeftRadius = 6;
            thumb.style.borderBottomRightRadius = 6;
            thumb.style.backgroundColor = _theme.SurfaceContainerHigh;
            thumb.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            row.Add(thumb);

            // Text column
            var textCol = new VisualElement();
            textCol.style.flexGrow = 1;
            textCol.style.marginLeft = 10;
            textCol.style.overflow = Overflow.Hidden;

            var avatarLabel = new Label();
            avatarLabel.name = "avatar";
            avatarLabel.style.fontSize = 13;
            avatarLabel.style.color = _theme.OnSurface;
            avatarLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            avatarLabel.style.overflow = Overflow.Hidden;
            avatarLabel.style.textOverflow = TextOverflow.Ellipsis;
            avatarLabel.style.whiteSpace = WhiteSpace.NoWrap;
            textCol.Add(avatarLabel);

            var shopLabel = new Label();
            shopLabel.name = "shop";
            shopLabel.style.fontSize = 11;
            shopLabel.style.color = _theme.OnSurfaceVariant;
            shopLabel.style.overflow = Overflow.Hidden;
            shopLabel.style.textOverflow = TextOverflow.Ellipsis;
            shopLabel.style.whiteSpace = WhiteSpace.NoWrap;
            textCol.Add(shopLabel);

            row.Add(textCol);

            // Conv badge
            var convBadge = new Label();
            convBadge.name = "conv";
            convBadge.style.fontSize = 10;
            convBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            convBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
            convBadge.style.width = 44;
            convBadge.style.height = 20;
            convBadge.style.borderTopLeftRadius = 10;
            convBadge.style.borderTopRightRadius = 10;
            convBadge.style.borderBottomLeftRadius = 10;
            convBadge.style.borderBottomRightRadius = 10;
            convBadge.style.flexShrink = 0;
            row.Add(convBadge);

            // Price
            var priceLabel = new Label();
            priceLabel.name = "price";
            priceLabel.style.fontSize = 12;
            priceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            priceLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            priceLabel.style.width = 56;
            priceLabel.style.flexShrink = 0;
            row.Add(priceLabel);

            return row;
        }

        private void BindListItem(MochiFitterProfileEntry p, VisualElement el, int index)
        {
            el.Q<Label>("avatar").text = p.avatar ?? "";
            el.Q<Label>("shop").text = p.shop ?? "";
            el.Q<Label>("price").text = p.price ?? "";

            // Price color
            var priceLabel = el.Q<Label>("price");
            bool free = IsFree(p.price);
            priceLabel.style.color = free ? _theme.Primary : _theme.Tertiary;

            // Conv badge
            var convBadge = el.Q<Label>("conv");
            SetConvBadge(convBadge, p.convType);

            // Thumbnail
            var thumb = el.Q("thumb");
            var tex = GetCachedThumbnail(p.boothId);
            if (tex != null)
            {
                thumb.style.backgroundImage = new StyleBackground(tex);
            }
            else
            {
                thumb.style.backgroundImage = StyleKeyword.None;
                // Lazy-load thumbnail
                RequestThumbnailLoad(p, () =>
                {
                    if (_virtualList != null) _virtualList.RefreshItem(index);
                });
            }

            // Selection highlight
            bool isSelected = index == _selectedIndex;
            el.style.backgroundColor = isSelected ? _theme.SecondaryContainer : StyleKeyword.None;
        }

        private void SetConvBadge(Label badge, string convType)
        {
            switch (convType)
            {
                case "both":
                    badge.text = M("両");
                    badge.style.color = new Color(0.15f, 0.4f, 0.15f);
                    badge.style.backgroundColor = new Color(0.4f, 0.85f, 0.4f, 0.2f);
                    break;
                case "forward":
                    badge.text = M("順");
                    badge.style.color = _theme.Primary;
                    badge.style.backgroundColor = new Color(_theme.Primary.r, _theme.Primary.g, _theme.Primary.b, 0.15f);
                    break;
                case "reverse":
                    badge.text = M("逆");
                    badge.style.color = _theme.Tertiary;
                    badge.style.backgroundColor = new Color(_theme.Tertiary.r, _theme.Tertiary.g, _theme.Tertiary.b, 0.15f);
                    break;
                default:
                    badge.text = "?";
                    badge.style.color = _theme.OnSurfaceVariant;
                    badge.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.12f);
                    break;
            }
        }

        // ── Detail Panel ────────────────────────────────────────────
        private void OnItemClicked(int index, MochiFitterProfileEntry entry)
        {
            _selectedIndex = index;
            _selectedEntry = entry;
            _virtualList?.RefreshAll();
            ShowDetail(entry);
        }

        private void ShowEmptyDetail()
        {
            _detailScroll.Clear();
            var placeholder = new Label(M("プロファイルを選択してください"));
            placeholder.style.color = _theme.OnSurfaceVariant;
            placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
            placeholder.style.flexGrow = 1;
            placeholder.style.marginTop = 40;
            _detailScroll.Add(placeholder);
        }

        private void ShowDetail(MochiFitterProfileEntry p)
        {
            _detailScroll.Clear();

            // Large thumbnail
            var thumbContainer = new VisualElement();
            thumbContainer.style.width = 250;
            thumbContainer.style.height = 180;
            thumbContainer.style.borderTopLeftRadius = 12;
            thumbContainer.style.borderTopRightRadius = 12;
            thumbContainer.style.borderBottomLeftRadius = 12;
            thumbContainer.style.borderBottomRightRadius = 12;
            thumbContainer.style.backgroundColor = _theme.SurfaceContainerHigh;
            thumbContainer.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            thumbContainer.style.alignSelf = Align.Center;
            thumbContainer.style.marginBottom = 12;

            var tex = GetCachedThumbnail(p.boothId);
            if (tex != null)
            {
                thumbContainer.style.backgroundImage = new StyleBackground(tex);
            }
            else
            {
                RequestThumbnailLoad(p, () => ShowDetail(p));
            }
            _detailScroll.Add(thumbContainer);

            // Avatar name
            var title = new Label(p.avatar);
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = _theme.OnSurface;
            title.style.whiteSpace = WhiteSpace.Normal;
            _detailScroll.Add(title);

            AddSpace(_detailScroll, 8);

            // Info fields
            AddDetailField(M("ショップ"), p.shop);
            AddDetailField(M("価格"), p.price);
            AddDetailField(M("変換タイプ"), GetConvDisplayName(p.convType));
            AddDetailField("BOOTH ID", p.boothId);

            AddSpace(_detailScroll, 12);

            // BOOTH button
            var boothBtn = new MD3Button(M("BOOTHで開く"), MD3ButtonStyle.Filled);
            boothBtn.style.alignSelf = Align.Stretch;
            boothBtn.clicked += () => Application.OpenURL($"https://booth.pm/ja/items/{p.boothId}");
            _detailScroll.Add(boothBtn);

        }

        private void AddDetailField(string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 4;

            var lbl = new Label(label);
            lbl.style.fontSize = 11;
            lbl.style.color = _theme.OnSurfaceVariant;
            lbl.style.width = 80;
            lbl.style.flexShrink = 0;
            row.Add(lbl);

            var val = new Label(value ?? "");
            val.style.fontSize = 12;
            val.style.color = _theme.OnSurface;
            val.style.flexGrow = 1;
            val.style.whiteSpace = WhiteSpace.Normal;
            row.Add(val);

            _detailScroll.Add(row);
        }

        private void AddSpace(VisualElement parent, float height)
        {
            var spacer = new VisualElement();
            spacer.style.height = height;
            parent.Add(spacer);
        }

        // ── Filter ──────────────────────────────────────────────────
        private void ApplyFilter()
        {
            if (_catalog == null || _catalog.profiles == null)
            {
                _filtered = new List<MochiFitterProfileEntry>();
                return;
            }

            string kw = _searchField != null && !string.IsNullOrEmpty(_searchField.Value)
                ? _searchField.Value.ToLower() : null;
            string convFilter = _filterConvIdx > 0 && _filterConvIdx < ConvFilterValues.Length
                ? ConvFilterValues[_filterConvIdx] : null;

            _filtered = _catalog.profiles.Where(p =>
            {
                if (convFilter != null && p.convType != convFilter) return false;

                if (_filterPriceIdx == 1 && !IsFree(p.price)) return false;
                if (_filterPriceIdx == 2 && IsFree(p.price)) return false;

                if (kw != null)
                {
                    if (!(p.avatar ?? "").ToLower().Contains(kw) &&
                        !(p.shop ?? "").ToLower().Contains(kw))
                        return false;
                }

                return true;
            }).ToList();

            if (_selectedIndex >= _filtered.Count)
            {
                _selectedIndex = -1;
                _selectedEntry = null;
            }
        }

        private void RefreshView()
        {
            if (_isGridMode)
                RebuildGrid();
            else
                _virtualList?.SetData(_filtered, BindListItem, MakeListItem);
            UpdateStatus();
        }

        private void SwitchViewMode()
        {
            _listHost.style.display = _isGridMode ? DisplayStyle.None : DisplayStyle.Flex;
            _gridHost.style.display = _isGridMode ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshView();
        }

        // ── Grid View ───────────────────────────────────────────────
        private void RebuildGrid()
        {
            if (_gridContainer == null) return;
            _gridContainer.Clear();

            if (_filtered == null || _filtered.Count == 0)
            {
                var placeholder = new Label(M("該当するプロファイルがありません"));
                placeholder.style.color = _theme.OnSurfaceVariant;
                placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
                placeholder.style.width = Length.Percent(100);
                placeholder.style.marginTop = 40;
                _gridContainer.Add(placeholder);
                return;
            }

            for (int i = 0; i < _filtered.Count; i++)
            {
                var entry = _filtered[i];
                var idx = i;
                var card = MakeGridCard(entry, idx);
                _gridContainer.Add(card);
            }
        }

        private VisualElement MakeGridCard(MochiFitterProfileEntry p, int index)
        {
            var card = new VisualElement();
            card.style.minWidth = GridCardWidth;
            card.style.maxWidth = GridCardWidth * 1.6f;
            card.style.flexGrow = 1;
            card.style.flexBasis = GridCardWidth;
            card.style.marginRight = GridGap;
            card.style.marginBottom = GridGap;
            card.style.borderTopLeftRadius = 10;
            card.style.borderTopRightRadius = 10;
            card.style.borderBottomLeftRadius = 10;
            card.style.borderBottomRightRadius = 10;
            card.style.backgroundColor = _theme.SurfaceContainerHigh;
            card.style.overflow = Overflow.Hidden;

            // Thumbnail area — use paddingTop % trick for aspect ratio 1:1
            var thumbWrapper = new VisualElement();
            thumbWrapper.style.width = Length.Percent(100);
            thumbWrapper.style.paddingTop = Length.Percent(100); // 1:1 aspect ratio
            thumbWrapper.style.position = Position.Relative;

            var thumbArea = new VisualElement();
            thumbArea.style.position = Position.Absolute;
            thumbArea.style.top = 0;
            thumbArea.style.left = 0;
            thumbArea.style.right = 0;
            thumbArea.style.bottom = 0;
            thumbArea.style.backgroundColor = _theme.SurfaceContainerHighest;
            thumbArea.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;

            var tex = GetCachedThumbnail(p.boothId);
            if (tex != null)
            {
                thumbArea.style.backgroundImage = new StyleBackground(tex);
            }
            else
            {
                RequestThumbnailLoad(p, () =>
                {
                    var loaded = GetCachedThumbnail(p.boothId);
                    if (loaded != null)
                        thumbArea.style.backgroundImage = new StyleBackground(loaded);
                });
            }
            thumbWrapper.Add(thumbArea);
            card.Add(thumbWrapper);

            // Info area below thumbnail
            var info = new VisualElement();
            info.style.paddingLeft = 6;
            info.style.paddingRight = 6;
            info.style.paddingTop = 4;
            info.style.paddingBottom = 4;

            var nameLabel = new Label(p.avatar ?? "");
            nameLabel.style.fontSize = 11;
            nameLabel.style.color = _theme.OnSurface;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
            info.Add(nameLabel);

            // Price + conv badge row
            var badgeRow = new VisualElement();
            badgeRow.style.flexDirection = FlexDirection.Row;
            badgeRow.style.justifyContent = Justify.SpaceBetween;
            badgeRow.style.alignItems = Align.Center;
            badgeRow.style.marginTop = 2;

            var priceLabel = new Label(p.price ?? "");
            priceLabel.style.fontSize = 10;
            priceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            priceLabel.style.color = IsFree(p.price) ? _theme.Primary : _theme.Tertiary;
            badgeRow.Add(priceLabel);

            var convLabel = new Label();
            convLabel.style.fontSize = 9;
            convLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            convLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            convLabel.style.paddingLeft = 4;
            convLabel.style.paddingRight = 4;
            convLabel.style.height = 16;
            convLabel.style.borderTopLeftRadius = 8;
            convLabel.style.borderTopRightRadius = 8;
            convLabel.style.borderBottomLeftRadius = 8;
            convLabel.style.borderBottomRightRadius = 8;
            SetConvBadge(convLabel, p.convType);
            badgeRow.Add(convLabel);

            info.Add(badgeRow);
            card.Add(info);

            // Hover and click
            card.RegisterCallback<MouseEnterEvent>(_ =>
                card.style.backgroundColor = _theme.HoverOverlay(_theme.SurfaceContainerHigh, _theme.OnSurface));
            card.RegisterCallback<MouseLeaveEvent>(_ =>
                card.style.backgroundColor = _theme.SurfaceContainerHigh);
            card.RegisterCallback<ClickEvent>(_ =>
            {
                _selectedIndex = index;
                _selectedEntry = p;
                ShowDetail(p);
            });

            return card;
        }

        private void UpdateStatus()
        {
            if (_statusLabel == null) return;
            int total = _catalog?.profiles?.Count ?? 0;
            int shown = _filtered?.Count ?? 0;
            int thumbCount = _catalog?.profiles?.Count(p => !string.IsNullOrEmpty(p.thumbnailUrl)) ?? 0;
            _statusLabel.text = $"{shown}/{total} {M("件表示")} | {M("サムネイル")}: {thumbCount}/{total}";
        }

        // ── Thumbnail lazy loading ──────────────────────────────────
        private Texture2D GetCachedThumbnail(string boothId)
        {
            if (string.IsNullOrEmpty(boothId)) return null;

            // Memory cache
            if (_thumbCache.TryGetValue(boothId, out var cached)) return cached;

            // Disk cache
            string diskPath = Path.Combine(ThumbCacheDir, boothId + ".png");
            if (File.Exists(diskPath))
            {
                try
                {
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(File.ReadAllBytes(diskPath));
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    _thumbCache[boothId] = tex;
                    return tex;
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Enqueue a thumbnail load request. Items bound most recently (= currently visible in viewport)
        /// are inserted at the front of the queue so they load first.
        /// </summary>
        private void RequestThumbnailLoad(MochiFitterProfileEntry entry, Action onComplete)
        {
            if (string.IsNullOrEmpty(entry?.boothId)) return;
            if (_thumbCache.ContainsKey(entry.boothId)) return;
            if (_thumbLoading.Contains(entry.boothId)) return;
            if (_thumbFailed.Contains(entry.boothId)) return;
            if (string.IsNullOrEmpty(entry.thumbnailUrl)) return;

            // Remove any existing stale request for this boothId (it scrolled away and back)
            var node = _thumbQueue.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value.entry.boothId == entry.boothId)
                    _thumbQueue.Remove(node);
                node = next;
            }

            // Insert at front = highest priority (most recently bound = visible now)
            _thumbQueue.AddFirst(new ThumbRequest { entry = entry, onComplete = onComplete });

            EnsureThumbPump();
        }

        private void EnsureThumbPump()
        {
            if (_thumbPumpRegistered) return;
            _thumbPumpRegistered = true;
            EditorApplication.update += ThumbPump;
        }

        private void StopThumbPump()
        {
            if (!_thumbPumpRegistered) return;
            _thumbPumpRegistered = false;
            EditorApplication.update -= ThumbPump;
        }

        /// <summary>
        /// Called every editor frame. Drains the queue up to MaxConcurrentThumbLoads.
        /// </summary>
        private void ThumbPump()
        {
            while (_activeThumbLoads < MaxConcurrentThumbLoads && _thumbQueue.Count > 0)
            {
                var req = _thumbQueue.First.Value;
                _thumbQueue.RemoveFirst();

                // Skip if already loaded / loading / no URL
                if (_thumbCache.ContainsKey(req.entry.boothId)) continue;
                if (_thumbLoading.Contains(req.entry.boothId)) continue;
                if (string.IsNullOrEmpty(req.entry.thumbnailUrl)) continue;

                StartThumbDownload(req.entry, req.onComplete);
            }

            // Unregister pump when idle
            if (_thumbQueue.Count == 0 && _activeThumbLoads == 0)
                StopThumbPump();
        }

        private void StartThumbDownload(MochiFitterProfileEntry entry, Action onComplete)
        {
            _thumbLoading.Add(entry.boothId);
            _activeThumbLoads++;

            var request = UnityWebRequestTexture.GetTexture(entry.thumbnailUrl);
            var op = request.SendWebRequest();
            string boothId = entry.boothId; // capture for closure

            op.completed += _ =>
            {
                _activeThumbLoads--;
                _thumbLoading.Remove(boothId);

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var tex = DownloadHandlerTexture.GetContent(request);
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    _thumbCache[boothId] = tex;

                    try
                    {
                        if (!Directory.Exists(ThumbCacheDir)) Directory.CreateDirectory(ThumbCacheDir);
                        File.WriteAllBytes(Path.Combine(ThumbCacheDir, boothId + ".png"), tex.EncodeToPNG());
                    }
                    catch { }

                    onComplete?.Invoke();
                }
                else
                {
                    _thumbFailed.Add(boothId);
                }

                request.Dispose();

                // Pump may have more work
                EnsureThumbPump();
            };
        }

        // ── Batch thumbnail URL fetch ───────────────────────────────
        private void FetchThumbnailUrls()
        {
            if (_catalog == null || _isFetchingUrls) return;

            var missing = _catalog.profiles.Where(p => string.IsNullOrEmpty(p.thumbnailUrl)).ToList();
            if (missing.Count == 0)
            {
                EditorUtility.DisplayDialog(M("完了"), M("全てのサムネイルURLは取得済みです。"), "OK");
                return;
            }

            _isFetchingUrls = true;
            _fetchIdx = 0;
            _fetchReq = null;
            EditorApplication.update += FetchUrlStep;
        }

        private void StopFetchUrls()
        {
            if (!_isFetchingUrls) return;
            _isFetchingUrls = false;
            EditorApplication.update -= FetchUrlStep;
            EditorUtility.ClearProgressBar();
            _fetchReq?.Dispose();
            _fetchReq = null;
            SaveCatalogStatic(_catalog);
        }

        private void FetchUrlStep()
        {
            // Process completed request
            if (_fetchReq != null)
            {
                if (!_fetchReq.isDone) return;

                if (_fetchReq.result == UnityWebRequest.Result.Success)
                {
                    string json = _fetchReq.downloadHandler.text;
                    string thumbUrl = ExtractThumbnailUrl(json);

                    var missing = _catalog.profiles.Where(p => string.IsNullOrEmpty(p.thumbnailUrl)).ToList();
                    int completedIdx = _fetchIdx - 1;
                    if (!string.IsNullOrEmpty(thumbUrl) && completedIdx >= 0 && completedIdx < missing.Count)
                        missing[completedIdx].thumbnailUrl = thumbUrl;
                }

                _fetchReq.Dispose();
                _fetchReq = null;

                if (_fetchIdx % 20 == 0) SaveCatalogStatic(_catalog);
            }

            var missingList = _catalog.profiles.Where(p => string.IsNullOrEmpty(p.thumbnailUrl)).ToList();
            if (_fetchIdx >= missingList.Count)
            {
                StopFetchUrls();
                _virtualList?.RefreshAll();
                UpdateStatus();
                return;
            }

            var target = missingList[_fetchIdx];
            _fetchReq = UnityWebRequest.Get($"https://booth.pm/ja/items/{target.boothId}.json");
            _fetchReq.SendWebRequest();
            _fetchIdx++;

            float progress = (float)_fetchIdx / missingList.Count;
            if (EditorUtility.DisplayCancelableProgressBar(
                M("サムネイルURL取得中"),
                $"{_fetchIdx}/{missingList.Count}: {target.avatar}",
                progress))
            {
                StopFetchUrls();
                _virtualList?.RefreshAll();
                UpdateStatus();
            }
        }

        private static string ExtractThumbnailUrl(string json)
        {
            int imagesIdx = json.IndexOf("\"images\"");
            if (imagesIdx < 0) return null;

            int originalIdx = json.IndexOf("\"original\"", imagesIdx);
            if (originalIdx < 0) return null;

            int colonIdx = json.IndexOf(':', originalIdx + 10);
            if (colonIdx < 0) return null;

            int urlStart = json.IndexOf('"', colonIdx + 1);
            if (urlStart < 0) return null;

            int urlEnd = json.IndexOf('"', urlStart + 1);
            if (urlEnd < 0) return null;

            return json.Substring(urlStart + 1, urlEnd - urlStart - 1);
        }

        // ── Helpers ─────────────────────────────────────────────────
        private static bool IsFree(string price)
        {
            if (string.IsNullOrEmpty(price)) return false;
            return price == "無料" || price == "¥0" || price == "0" || price.Contains("無料");
        }

        private static string GetConvDisplayName(string convType)
        {
            switch (convType)
            {
                case "both": return M("両変換（順＋逆）");
                case "forward": return M("順変換のみ");
                case "reverse": return M("逆変換のみ");
                default: return M("不明");
            }
        }

        // ── Static API (shared with AgentTools) ─────────────────────
        internal static MochiFitterCatalogRoot LoadCatalogStatic()
        {
            string path = GetCatalogPath();
            if (!File.Exists(path)) return new MochiFitterCatalogRoot();

            try
            {
                return ParseCatalog(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"[MochiFitterCatalog] Load failed: {e.Message}");
                return new MochiFitterCatalogRoot();
            }
        }

        internal static void SaveCatalogStatic(MochiFitterCatalogRoot catalog)
        {
            string path = GetCatalogPath();
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, SerializeCatalog(catalog));
            }
            catch (Exception e)
            {
                Debug.LogError($"[MochiFitterCatalog] Save failed: {e.Message}");
            }
        }

        internal static string GetCatalogPath()
        {
            string fullPath = Path.GetFullPath(CatalogPath);
            if (File.Exists(fullPath)) return fullPath;

            string[] guids = AssetDatabase.FindAssets("mochifitter_catalog t:TextAsset");
            if (guids.Length > 0) return Path.GetFullPath(AssetDatabase.GUIDToAssetPath(guids[0]));

            return fullPath;
        }

        // ── JSON ────────────────────────────────────────────────────
        private static MochiFitterCatalogRoot ParseCatalog(string json)
        {
            var root = new MochiFitterCatalogRoot();

            int profilesStart = json.IndexOf("\"profiles\"");
            if (profilesStart < 0) return root;

            int bracketStart = json.IndexOf('[', profilesStart);
            int bracketEnd = FindMatchingBracket(json, bracketStart);
            if (bracketEnd < 0) return root;

            int pos = bracketStart + 1;
            while (pos < bracketEnd)
            {
                int objStart = json.IndexOf('{', pos);
                if (objStart < 0 || objStart > bracketEnd) break;
                int objEnd = FindMatchingBrace(json, objStart);
                if (objEnd < 0) break;

                string objJson = json.Substring(objStart, objEnd - objStart + 1);
                var entry = JsonUtility.FromJson<MochiFitterProfileEntry>(objJson);
                if (entry != null) root.profiles.Add(entry);

                pos = objEnd + 1;
            }

            int verIdx = json.IndexOf("\"version\"");
            if (verIdx >= 0)
            {
                int colon = json.IndexOf(':', verIdx);
                if (colon >= 0)
                {
                    int numStart = colon + 1;
                    while (numStart < json.Length && !char.IsDigit(json[numStart])) numStart++;
                    int numEnd = numStart;
                    while (numEnd < json.Length && char.IsDigit(json[numEnd])) numEnd++;
                    if (numEnd > numStart && int.TryParse(json.Substring(numStart, numEnd - numStart), out int ver))
                        root.version = ver;
                }
            }

            return root;
        }

        private static string SerializeCatalog(MochiFitterCatalogRoot catalog)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"version\": {catalog.version},");
            sb.AppendLine("  \"profiles\": [");

            for (int i = 0; i < catalog.profiles.Count; i++)
            {
                var p = catalog.profiles[i];
                sb.Append($"    {{\"avatar\": \"{Esc(p.avatar)}\", \"price\": \"{Esc(p.price)}\", \"convType\": \"{Esc(p.convType)}\", \"shop\": \"{Esc(p.shop)}\", \"boothId\": \"{Esc(p.boothId)}\"");
                if (!string.IsNullOrEmpty(p.thumbnailUrl))
                    sb.Append($", \"thumbnailUrl\": \"{Esc(p.thumbnailUrl)}\"");
                sb.Append("}");
                sb.AppendLine(i < catalog.profiles.Count - 1 ? "," : "");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }

        private static int FindMatchingBrace(string s, int openPos)
        {
            int depth = 0; bool inStr = false;
            for (int i = openPos; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' && (i == 0 || s[i - 1] != '\\')) inStr = !inStr;
                if (inStr) continue;
                if (c == '{') depth++;
                if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static int FindMatchingBracket(string s, int openPos)
        {
            int depth = 0; bool inStr = false;
            for (int i = openPos; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' && (i == 0 || s[i - 1] != '\\')) inStr = !inStr;
                if (inStr) continue;
                if (c == '[') depth++;
                if (c == ']') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }
    }
}
