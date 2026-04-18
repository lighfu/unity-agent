using UnityEditor;
using UnityEngine;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    internal class SupporterShowcaseWindow : EditorWindow
    {
        // ─── 定数 ───
        const float IconDisplaySize = 64f;
        const float CellPadding = 32f;
        const float NameHeight = 20f;
        const float BobAmplitude = 6f;
        const float BobSpeed = 1.5f;
        const float TitleHeight = 48f;

        const int GlowTexSize = 96;
        const int SparkleTexSize = 8;
        const int SparklesPerIcon = 6;
        const float SparkleOrbitRadius = 40f;
        const float SparkleRotSpeed = 1.2f;
        const int BackgroundStarCount = 60;

        const int RippleTexSize = 64;
        const int RippleCount = 3;
        const float RippleCycleDuration = 3.5f;
        const float RippleMaxScale = 2.2f;

        const int VignetteTexSize = 128;
        const int AuroraBeamCount = 3;
        const int FloatingParticleCount = 40;

        // ─── プロシージャルテクスチャ ───
        Texture2D _glowTex;
        Texture2D _sparkleTex;
        Texture2D _rippleTex;
        Texture2D _vignetteTex;
        Texture2D _softDotTex;
        Texture2D _roundedFillTex;
        Texture2D _roundedBorderTex;

        const int RoundedTexSize = 48;
        const int RoundedCornerRadius = 12;

        // ─── スクロール ───
        Vector2 _scrollPos;

        // ─── 背景星データ ───
        Vector2[] _starPositions;
        float[] _starPhases;
        float[] _starSizes;

        // ─── 浮遊パーティクル ───
        Vector2[] _particlePos;
        Vector2[] _particleVel;
        float[] _particlePhase;
        float[] _particleSizes;

        // ─── メッセージポップアップ ───
        int _selectedIndex = -1;
        float _popupOpenTime;
        const float PopupFadeDuration = 0.2f;

        internal static void Open()
        {
            var win = GetWindow<SupporterShowcaseWindow>(true, M("サポーター"), true);
            win.minSize = new Vector2(320, 300);
        }

        void OnEnable()
        {
            _glowTex = GenerateGlow(GlowTexSize);
            _sparkleTex = GenerateSparkle(SparkleTexSize);
            _rippleTex = GenerateRing(RippleTexSize);
            _vignetteTex = GenerateVignette(VignetteTexSize);
            _softDotTex = GenerateGlow(16);
            _roundedFillTex = GenerateRoundedRect(RoundedTexSize, RoundedCornerRadius, false);
            _roundedBorderTex = GenerateRoundedRect(RoundedTexSize, RoundedCornerRadius, true);
            foreach (var tex in new[] { _glowTex, _sparkleTex, _rippleTex, _vignetteTex, _softDotTex, _roundedFillTex, _roundedBorderTex })
                tex.hideFlags = HideFlags.HideAndDontSave;
            InitBackgroundStars();
            InitFloatingParticles();
            EditorApplication.update += Repaint;
        }

        void OnDisable()
        {
            EditorApplication.update -= Repaint;
            if (_glowTex != null) DestroyImmediate(_glowTex);
            if (_sparkleTex != null) DestroyImmediate(_sparkleTex);
            if (_rippleTex != null) DestroyImmediate(_rippleTex);
            if (_vignetteTex != null) DestroyImmediate(_vignetteTex);
            if (_softDotTex != null) DestroyImmediate(_softDotTex);
            if (_roundedFillTex != null) DestroyImmediate(_roundedFillTex);
            if (_roundedBorderTex != null) DestroyImmediate(_roundedBorderTex);
        }

        // ─── 背景星の初期化 ───
        void InitBackgroundStars()
        {
            _starPositions = new Vector2[BackgroundStarCount];
            _starPhases = new float[BackgroundStarCount];
            _starSizes = new float[BackgroundStarCount];

            // 決定論的シード
            var rng = new System.Random(42);
            for (int i = 0; i < BackgroundStarCount; i++)
            {
                _starPositions[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                _starPhases[i] = (float)(rng.NextDouble() * Mathf.PI * 2);
                _starSizes[i] = 1f + (float)(rng.NextDouble() * 2f);
            }
        }

        void InitFloatingParticles()
        {
            _particlePos = new Vector2[FloatingParticleCount];
            _particleVel = new Vector2[FloatingParticleCount];
            _particlePhase = new float[FloatingParticleCount];
            _particleSizes = new float[FloatingParticleCount];

            var rng = new System.Random(77);
            for (int i = 0; i < FloatingParticleCount; i++)
            {
                _particlePos[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                _particleVel[i] = new Vector2(
                    ((float)rng.NextDouble() - 0.5f) * 0.01f,
                    -0.003f - (float)rng.NextDouble() * 0.008f); // ゆっくり上昇
                _particlePhase[i] = (float)(rng.NextDouble() * Mathf.PI * 2);
                _particleSizes[i] = 3f + (float)(rng.NextDouble() * 5f);
            }
        }

        void OnGUI()
        {
            var t = (float)EditorApplication.timeSinceStartup;
            var fullRect = new Rect(0, 0, position.width, position.height);

            // ─── 背景 ───
            DrawRect(fullRect, new Color(0.12f, 0.12f, 0.16f, 1f));
            DrawBackgroundStars(fullRect, t);
            DrawAuroraBeams(fullRect, t);

            // ─── タイトル ───
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.75f, 0.85f, 1f) }
            };
            var titleRect = new Rect(0, 8, position.width, TitleHeight);
            GUI.Label(titleRect, M("♡ Supporters ♡"), titleStyle);

            // ─── 感謝メッセージ ───
            var msgStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = new Color(0.78f, 0.78f, 0.85f, 1f) }
            };
            var msgRect = new Rect(0, TitleHeight + 4, position.width, 20);
            GUI.Label(msgRect, M("支援してくれてありがとうございます！"), msgStyle);

            // ─── 空の場合 ───
            var supporters = SupporterData.All;
            if (supporters.Length == 0)
            {
                var emptyStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.6f, 0.6f, 0.7f, 1f) }
                };
                var emptyRect = new Rect(0, TitleHeight + 40, position.width, 40);
                GUI.Label(emptyRect, M("— まだサポーターはいません —"), emptyStyle);
                return;
            }

            // ─── グリッドレイアウト計算 ───
            float cellW = IconDisplaySize + CellPadding;
            float cellH = IconDisplaySize + NameHeight + CellPadding + BobAmplitude * 2;
            int cols = Mathf.Max(1, Mathf.FloorToInt((position.width - CellPadding) / cellW));
            int rows = Mathf.CeilToInt((float)supporters.Length / cols);

            float gridContentW = cols * cellW;
            float buttonAreaH = 40f;
            float gridContentH = rows * cellH + CellPadding + buttonAreaH;
            float contentTop = TitleHeight + 32;
            var scrollArea = new Rect(0, contentTop, position.width, position.height - contentTop);

            // ─── ScrollView ───
            var viewRect = new Rect(0, 0, scrollArea.width - 16, gridContentH);
            _scrollPos = GUI.BeginScrollView(scrollArea, _scrollPos, viewRect);

            for (int i = 0; i < supporters.Length; i++)
            {
                var supporter = supporters[i];
                int col = i % cols;
                int row = i / cols;

                // 行の中央寄せ
                int itemsInRow = (row < rows - 1) ? cols : supporters.Length - row * cols;
                float rowOffsetX = (viewRect.width - itemsInRow * cellW) * 0.5f;

                // セル中心（固定位置）
                float phase = i * 0.8f;
                float baseCx = rowOffsetX + col * cellW + cellW * 0.5f;
                float baseCy = row * cellH + CellPadding + BobAmplitude;

                // ぷかぷか浮遊（アイコンのみ）
                float bobY = Mathf.Sin(t * BobSpeed + phase) * BobAmplitude;
                float driftX = Mathf.Sin(t * 0.7f + phase * 1.3f) * 2f;
                float cx = baseCx + driftX;
                float cy = baseCy + bobY;

                // ─── 波紋 ───
                float iconCenterX = cx;
                float iconCenterY = cy + IconDisplaySize * 0.5f;
                for (int r = 0; r < RippleCount; r++)
                {
                    float progress = (t / RippleCycleDuration + (float)r / RippleCount + phase * 0.1f) % 1f;
                    float rippleSize = IconDisplaySize * (1f + progress * (RippleMaxScale - 1f));
                    float rippleAlpha = (1f - progress) * (1f - progress) * 0.35f;
                    var rippleRect = new Rect(
                        iconCenterX - rippleSize * 0.5f,
                        iconCenterY - rippleSize * 0.5f,
                        rippleSize, rippleSize);
                    var prevCol = GUI.color;
                    GUI.color = new Color(0.5f, 0.6f, 1f, rippleAlpha);
                    GUI.DrawTexture(rippleRect, _rippleTex);
                    GUI.color = prevCol;
                }

                // ─── グロー ───
                float glowAlpha = 0.3f + 0.15f * Mathf.Sin(t * 2f + phase);
                float glowSize = IconDisplaySize * 1.5f;
                var glowRect = new Rect(cx - glowSize * 0.5f, cy - glowSize * 0.5f + IconDisplaySize * 0.5f, glowSize, glowSize);
                var prevColor = GUI.color;
                GUI.color = new Color(0.6f, 0.4f, 0.9f, glowAlpha);
                GUI.DrawTexture(glowRect, _glowTex);
                GUI.color = prevColor;

                // ─── アイコン ───
                var iconRect = new Rect(cx - IconDisplaySize * 0.5f, cy, IconDisplaySize, IconDisplaySize);
                var avatar = SupporterData.GetAvatar(supporter);

                bool hasUrl = !string.IsNullOrEmpty(supporter.url);
                bool hasMsg = !string.IsNullOrEmpty(supporter.message);
                bool clickable = hasUrl || hasMsg;
                bool isHover = iconRect.Contains(Event.current.mousePosition);

                // ホバー時拡大
                if (clickable && isHover)
                {
                    float scale = 1.08f;
                    float expand = IconDisplaySize * (scale - 1f);
                    iconRect = new Rect(
                        iconRect.x - expand * 0.5f,
                        iconRect.y - expand * 0.5f,
                        iconRect.width + expand,
                        iconRect.height + expand);
                    EditorGUIUtility.AddCursorRect(iconRect, MouseCursor.Link);
                }

                GUI.DrawTexture(iconRect, avatar, ScaleMode.ScaleToFit);

                // クリック
                if (clickable && Event.current.type == EventType.MouseDown
                    && Event.current.button == 0 && iconRect.Contains(Event.current.mousePosition))
                {
                    if (hasMsg)
                    {
                        _selectedIndex = (_selectedIndex == i) ? -1 : i;
                        _popupOpenTime = t;
                    }
                    else if (hasUrl)
                    {
                        Application.OpenURL(supporter.url);
                    }
                    Event.current.Use();
                }

                // ─── キラキラ ───
                for (int s = 0; s < SparklesPerIcon; s++)
                {
                    float angle = t * SparkleRotSpeed + phase + s * (Mathf.PI * 2f / SparklesPerIcon);
                    float sparkleAlpha = 0.5f + 0.5f * Mathf.Sin(t * 3f + s * 1.1f + phase);
                    float sx = cx + Mathf.Cos(angle) * SparkleOrbitRadius;
                    float sy = cy + IconDisplaySize * 0.5f + Mathf.Sin(angle) * SparkleOrbitRadius * 0.7f;

                    var sparkleRect = new Rect(sx - SparkleTexSize * 0.5f, sy - SparkleTexSize * 0.5f, SparkleTexSize, SparkleTexSize);
                    prevColor = GUI.color;
                    GUI.color = new Color(1f, 0.95f, 0.7f, sparkleAlpha * 0.8f);
                    GUI.DrawTexture(sparkleRect, _sparkleTex);
                    GUI.color = prevColor;
                }

                // ─── 名前ラベル ───
                var nameStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperCenter,
                    normal = { textColor = new Color(0.85f, 0.85f, 0.9f, 1f) }
                };
                var nameRect = new Rect(baseCx - cellW * 0.5f, baseCy + IconDisplaySize + 4, cellW, NameHeight);
                GUI.Label(nameRect, supporter.name, nameStyle);
            }

            // ─── Ko-fi リンクボタン ───
            float btnW = 200f;
            float btnH = 28f;
            float btnY = rows * cellH + CellPadding * 0.5f;
            var btnRect = new Rect((viewRect.width - btnW) * 0.5f, btnY, btnW, btnH);
            if (GUI.Button(btnRect, M("♡ Ko-fi でサポートする")))
            {
                Application.OpenURL("https://ko-fi.com/ajisaiflow");
            }

            GUI.EndScrollView();

            // ─── メッセージポップアップ ───
            DrawMessagePopup(fullRect, t);

            // ─── 全体エフェクト（最前面） ───
            DrawFloatingParticles(fullRect, t);
            DrawVignette(fullRect);

            // ─── ポップアップ外クリックで閉じる ───
            if (_selectedIndex >= 0 && Event.current.type == EventType.MouseDown)
            {
                _selectedIndex = -1;
                Event.current.Use();
            }
        }

        // ─── メッセージポップアップ描画 ───
        void DrawMessagePopup(Rect area, float t)
        {
            var supporters = SupporterData.All;
            if (_selectedIndex < 0 || _selectedIndex >= supporters.Length) return;

            var supporter = supporters[_selectedIndex];
            if (string.IsNullOrEmpty(supporter.message)) { _selectedIndex = -1; return; }

            // フェードイン
            float elapsed = t - _popupOpenTime;
            float fade = Mathf.Clamp01(elapsed / PopupFadeDuration);

            // ポップアップサイズ
            float popW = Mathf.Min(300f, area.width - 40f);
            float headerH = 18f;
            float nameH = 24f;
            float msgPad = 12f;

            // メッセージ高さを計算
            var msgStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.9f, 0.9f, 0.95f, fade) },
                padding = new RectOffset(0, 0, 0, 0)
            };
            float innerW = popW - msgPad * 2;
            float msgH = msgStyle.CalcHeight(new GUIContent(supporter.message), innerW);

            // URL ボタン行
            float urlRowH = string.IsNullOrEmpty(supporter.url) ? 0f : 28f;
            float popH = headerH + nameH + msgH + msgPad * 3.5f + urlRowH;

            float popX = (area.width - popW) * 0.5f;
            float popY = (area.height - popH) * 0.5f;
            var popRect = new Rect(popX, popY, popW, popH);

            // 半透明オーバーレイ
            DrawRect(area, new Color(0f, 0f, 0f, 0.4f * fade));

            // ポップアップ背景（角丸）
            DrawRoundedRect(popRect, new Color(0.15f, 0.15f, 0.22f, 0.95f * fade), _roundedFillTex);
            // 枠線（角丸）
            DrawRoundedRect(popRect, new Color(0.5f, 0.4f, 0.8f, 0.6f * fade), _roundedBorderTex);

            // ヘッダー「作者からの一言」
            var headerStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.65f, 0.65f, 0.75f, fade) }
            };
            GUI.Label(new Rect(popX, popY + msgPad * 0.5f, popW, headerH), M("— 作者からの一言 —"), headerStyle);

            // 名前 (To: ...)
            var nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.8f, 0.9f, fade) }
            };
            GUI.Label(new Rect(popX, popY + headerH + msgPad * 0.5f, popW, nameH),
                $"To: {supporter.name}", nameStyle);

            // メッセージ本文
            float msgTop = popY + headerH + nameH + msgPad * 1.5f;
            GUI.Label(new Rect(popX + msgPad, msgTop, innerW, msgH), supporter.message, msgStyle);

            // URL リンクボタン
            if (!string.IsNullOrEmpty(supporter.url))
            {
                float linkBtnW = 140f;
                float linkBtnH = 22f;
                float linkY = msgTop + msgH + msgPad * 0.5f;
                var linkRect = new Rect(popX + (popW - linkBtnW) * 0.5f, linkY, linkBtnW, linkBtnH);

                var linkStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    normal = { textColor = new Color(0.6f, 0.7f, 1f, fade) },
                    hover = { textColor = new Color(0.8f, 0.85f, 1f, fade) },
                    alignment = TextAnchor.MiddleCenter
                };
                if (GUI.Button(linkRect, M("♡ ページを開く"), linkStyle))
                {
                    Application.OpenURL(supporter.url);
                }
            }

            // ポップアップ内クリックを消費（外クリック閉じに巻き込まれないように）
            if (Event.current.type == EventType.MouseDown && popRect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
            }
        }

        // ─── 背景の星 ───
        void DrawBackgroundStars(Rect area, float t)
        {
            if (_starPositions == null) return;

            for (int i = 0; i < _starPositions.Length; i++)
            {
                float alpha = 0.15f + 0.15f * Mathf.Sin(t * 0.8f + _starPhases[i]);
                float x = _starPositions[i].x * area.width;
                float y = _starPositions[i].y * area.height;
                float size = _starSizes[i];

                var prevColor = GUI.color;
                GUI.color = new Color(0.8f, 0.8f, 1f, alpha);
                var starRect = new Rect(x - size * 0.5f, y - size * 0.5f, size, size);
                GUI.DrawTexture(starRect, _sparkleTex);
                GUI.color = prevColor;
            }
        }

        // ─── オーロラ光帯 ───
        void DrawAuroraBeams(Rect area, float t)
        {
            // 色の異なる帯がゆっくりウィンドウを横切る
            Color[] beamColors =
            {
                new Color(0.3f, 0.2f, 0.8f, 1f),
                new Color(0.2f, 0.6f, 0.7f, 1f),
                new Color(0.6f, 0.2f, 0.5f, 1f),
            };

            for (int i = 0; i < AuroraBeamCount; i++)
            {
                float speed = 0.08f + i * 0.03f;
                float phase = i * 2.1f;
                // X 位置が画面幅を往復
                float nx = Mathf.Sin(t * speed + phase) * 0.5f + 0.5f;
                float beamW = area.width * (0.25f + 0.1f * Mathf.Sin(t * 0.15f + i));
                float beamX = nx * area.width - beamW * 0.5f;
                float alpha = 0.04f + 0.02f * Mathf.Sin(t * 0.3f + i * 1.5f);

                var prevColor = GUI.color;
                GUI.color = new Color(beamColors[i].r, beamColors[i].g, beamColors[i].b, alpha);
                var beamRect = new Rect(beamX, 0, beamW, area.height);
                GUI.DrawTexture(beamRect, _glowTex, ScaleMode.StretchToFill);
                GUI.color = prevColor;
            }
        }

        // ─── 浮遊パーティクル ───
        void DrawFloatingParticles(Rect area, float t)
        {
            if (_particlePos == null) return;

            for (int i = 0; i < _particlePos.Length; i++)
            {
                // 正規化座標を更新（上昇 + 横揺れ）
                float px = _particlePos[i].x + Mathf.Sin(t * 0.5f + _particlePhase[i]) * 0.0003f;
                float py = _particlePos[i].y + _particleVel[i].y * 0.3f;

                // 画面外に出たら下から再出現
                if (py < -0.05f) py = 1.05f;
                if (px < -0.05f) px = 1.05f;
                if (px > 1.05f) px = -0.05f;
                _particlePos[i] = new Vector2(px, py);

                float x = px * area.width;
                float y = py * area.height;
                float size = _particleSizes[i];
                float alpha = 0.12f + 0.12f * Mathf.Sin(t * 1.2f + _particlePhase[i]);

                var prevColor = GUI.color;
                GUI.color = new Color(0.7f, 0.8f, 1f, alpha);
                var pRect = new Rect(x - size * 0.5f, y - size * 0.5f, size, size);
                GUI.DrawTexture(pRect, _softDotTex);
                GUI.color = prevColor;
            }
        }

        // ─── ビネット ───
        void DrawVignette(Rect area)
        {
            var prevColor = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(area, _vignetteTex, ScaleMode.StretchToFill);
            GUI.color = prevColor;
        }

        // ─── 角丸矩形描画（9スライス） ───
        void DrawRoundedRect(Rect rect, Color color, Texture2D tex)
        {
            var style = new GUIStyle
            {
                normal = { background = tex },
                border = new RectOffset(RoundedCornerRadius, RoundedCornerRadius, RoundedCornerRadius, RoundedCornerRadius)
            };
            if (Event.current.type == EventType.Repaint)
            {
                var prevColor = GUI.color;
                GUI.color = color;
                style.Draw(rect, GUIContent.none, false, false, false, false);
                GUI.color = prevColor;
            }
        }

        // ─── ユーティリティ ───
        static void DrawRect(Rect rect, Color color)
        {
            var prevColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
            GUI.color = prevColor;
        }

        // ─── プロシージャルテクスチャ生成 ───
        static Texture2D GenerateGlow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01(1f - d / radius);
                    float alpha = t * t; // 二次減衰
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        static Texture2D GenerateRoundedRect(int size, int cornerRadius, bool borderOnly)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(1f, 1f, 1f, 0f);
            var solid = new Color(1f, 1f, 1f, 1f);
            int r = cornerRadius;
            float borderW = 1.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 最寄りの角の中心からの距離を計算
                    float dx = 0f, dy = 0f;
                    bool inCorner = false;

                    if (x < r && y < r) { dx = r - x; dy = r - y; inCorner = true; }             // 左下
                    else if (x >= size - r && y < r) { dx = x - (size - 1 - r); dy = r - y; inCorner = true; }   // 右下
                    else if (x < r && y >= size - r) { dx = r - x; dy = y - (size - 1 - r); inCorner = true; }   // 左上
                    else if (x >= size - r && y >= size - r) { dx = x - (size - 1 - r); dy = y - (size - 1 - r); inCorner = true; } // 右上

                    if (inCorner)
                    {
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        if (dist > r + 0.5f)
                        {
                            tex.SetPixel(x, y, clear);
                            continue;
                        }
                        // アンチエイリアス (外周1px)
                        float edgeAA = Mathf.Clamp01(r + 0.5f - dist);

                        if (borderOnly)
                        {
                            float inner = Mathf.Clamp01(dist - (r - borderW));
                            tex.SetPixel(x, y, new Color(1f, 1f, 1f, inner * edgeAA));
                        }
                        else
                        {
                            tex.SetPixel(x, y, new Color(1f, 1f, 1f, edgeAA));
                        }
                    }
                    else
                    {
                        if (borderOnly)
                        {
                            // 辺の部分: 外周 borderW px のみ描画
                            bool onEdge = x < borderW || x >= size - borderW
                                       || y < borderW || y >= size - borderW;
                            tex.SetPixel(x, y, onEdge ? solid : clear);
                        }
                        else
                        {
                            tex.SetPixel(x, y, solid);
                        }
                    }
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        static Texture2D GenerateVignette(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float cx = (size - 1) * 0.5f;
            float cy = (size - 1) * 0.5f;
            float maxDist = Mathf.Sqrt(cx * cx + cy * cy);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / maxDist; // 0 中心 〜 1 角
                    // 中心は完全透明、外周に向かって黒く
                    float fade = Mathf.Clamp01(d - 0.3f) / 0.7f;  // 0.3 以内は透明
                    float alpha = fade * fade * 0.6f;
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        static Texture2D GenerateRing(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f;
            const float ringWidth = 0.12f; // リングの太さ (0〜1 の割合)

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / radius; // 0〜1+
                    // リング: 外周付近だけ描画、それ以外は透明
                    float ring = 1f - Mathf.Abs(d - (1f - ringWidth)) / ringWidth;
                    float alpha = Mathf.Clamp01(ring);
                    alpha *= alpha; // 柔らかい減衰
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        static Texture2D GenerateSparkle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(1f - d / radius); // 線形減衰
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
