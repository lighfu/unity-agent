using System;
using AjisaiFlow.MD3SDK.Editor;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public partial class UnityAgentWindow
    {
        // ═══════════════════════════════════════════════════════
        //  Update Notifications
        // ═══════════════════════════════════════════════════════

        private const string PrefLastSeenVersion = "UnityAgent_LastSeenVersion";
        private const string PrefDismissedUpdateVersion = "UnityAgent_DismissedUpdateVersion";

        private VisualElement _updateBanner;

        private void CheckAndShowUpdateBanner(bool forceCheck = false)
        {
            if (!forceCheck && UpdateChecker.Latest != null)
            {
                ShowUpdateBannerIfNeeded();
                return;
            }

            UpdateChecker.Checked += OnUpdateChecked;
            if (forceCheck)
                UpdateChecker.CheckNow();

            void OnUpdateChecked(VersionInfo _)
            {
                UpdateChecker.Checked -= OnUpdateChecked;
                ShowUpdateBannerIfNeeded();
            }
        }

        private void ShowUpdateBannerIfNeeded()
        {
            if (_updateBanner != null)
            {
                _updateBanner.RemoveFromHierarchy();
                _updateBanner = null;
            }

            if (!UpdateChecker.IsUpdateAvailable()) return;
            var latest = UpdateChecker.Latest;
            if (latest == null) return;

            var dismissed = SettingsStore.GetString(PrefDismissedUpdateVersion, "");
            if (dismissed == latest.version) return;

            _updateBanner = new VisualElement();
            _updateBanner.style.position = Position.Absolute;
            _updateBanner.style.bottom = 60;
            _updateBanner.style.left = 16;
            _updateBanner.style.right = 16;
            _updateBanner.style.maxWidth = 400;

            // 背景・文字色はカスタムテーマで `ErrorContainer` / `OnErrorContainer` が
            // 未設定 (alpha=0) だと完全に透明になり通知が見えなくなる。
            // 確実に定義されている SurfaceContainerHigh / OnSurface を主体にし、
            // それでも透明な場合はハードコードフォールバックを使う。
            Color bgColor = _theme.SurfaceContainerHigh.a > 0.1f
                ? _theme.SurfaceContainerHigh
                : new Color(0.16f, 0.15f, 0.18f, 1f); // dark gray fallback
            Color fgColor = _theme.OnSurface.a > 0.1f
                ? _theme.OnSurface
                : new Color(0.92f, 0.88f, 0.91f, 1f); // light text fallback
            Color accentColor = _theme.Primary.a > 0.1f
                ? _theme.Primary
                : new Color(0.82f, 0.74f, 1f, 1f); // purple fallback

            var card = new MD3Column(gap: MD3Spacing.XS);
            card.Padding(MD3Spacing.M);
            card.Radius(MD3Radius.L);
            card.style.backgroundColor = bgColor;
            card.style.borderLeftWidth = 4;
            card.style.borderLeftColor = accentColor;

            // Close button
            var closeBtn = new MD3IconButton(
                MD3Icon.Close,
                MD3IconButtonStyle.Standard,
                MD3IconButtonSize.Small);
            closeBtn.style.alignSelf = Align.FlexEnd;
            closeBtn.clicked += () =>
            {
                _updateBanner?.RemoveFromHierarchy();
                _updateBanner = null;
                SettingsStore.SetString(PrefDismissedUpdateVersion, latest.version);
            };
            card.Add(closeBtn);

            // Icon + title
            var iconRow = new MD3Row(gap: MD3Spacing.S);
            iconRow.style.alignItems = Align.Center;
            var icon = MD3Icon.Create(MD3Icon.SystemUpdate, 28f);
            icon.style.color = accentColor;
            iconRow.Add(icon);
            iconRow.Add(new MD3Text(
                M("新しいバージョンが利用可能です"),
                MD3TextStyle.TitleSmall, fgColor));
            card.Add(iconRow);

            // Version comparison
            card.Add(new MD3Text(
                $"v{UpdateChecker.CurrentVersion} → v{latest.version}",
                MD3TextStyle.Body, fgColor));

            // Instruction
            card.Add(new MD3Text(
                string.Format(M("ALCOM/VCC のプロジェクトのパッケージ管理から Unity AI Agent を v{0} に更新してください。"), latest.version),
                MD3TextStyle.BodySmall, fgColor));

            // Changelog
            if (!string.IsNullOrEmpty(latest.changelog))
            {
                card.Add(new MD3Divider());
                var changelogLabel = new MD3Text(
                    $"{M("更新内容")}:\n{latest.changelog}",
                    MD3TextStyle.BodySmall, fgColor);
                changelogLabel.style.whiteSpace = WhiteSpace.Normal;
                card.Add(changelogLabel);
            }

            // Action button
            var managerPath = FindProjectManager();
            if (managerPath != null)
            {
                var managerName = managerPath.Contains("ALCOM") ? "ALCOM" : "VCC";
                var updateBtn = new MD3Button(
                    $"{managerName} {M("を開く")}",
                    MD3ButtonStyle.Filled,
                    icon: MD3Icon.OpenInNew,
                    size: MD3ButtonSize.Small);
                updateBtn.clicked += () =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = managerPath,
                                UseShellExecute = true,
                            });
                    }
                    catch
                    {
                        Application.OpenURL(UpdateChecker.ProductPageUrl);
                    }
                };
                card.Add(updateBtn);
            }
            else
            {
                var updateBtn = new MD3Button(
                    M("商品ページを開く"),
                    MD3ButtonStyle.Filled,
                    icon: MD3Icon.OpenInNew,
                    size: MD3ButtonSize.Small);
                updateBtn.clicked += () => Application.OpenURL(UpdateChecker.ProductPageUrl);
                card.Add(updateBtn);
            }

            _updateBanner.Add(card);
            rootVisualElement.Add(_updateBanner);
        }

        private void ShowChangelogDialogIfNeeded()
        {
            var currentVersion = UpdateChecker.CurrentVersion;
            var lastSeen = SettingsStore.GetString(PrefLastSeenVersion, "");
            if (lastSeen == currentVersion) return;

            UpdateChecker.Checked += OnCheckedForChangelog;
            if (UpdateChecker.Latest != null)
                OnCheckedForChangelog(UpdateChecker.Latest);

            void OnCheckedForChangelog(VersionInfo info)
            {
                UpdateChecker.Checked -= OnCheckedForChangelog;

                if (info == null || string.IsNullOrEmpty(info.changelog)
                    || info.version != currentVersion)
                {
                    SettingsStore.SetString(PrefLastSeenVersion, currentVersion);
                    return;
                }

                var dialog = new MD3Dialog(
                    string.Format(M("v{0} の更新内容"), currentVersion),
                    info.changelog,
                    confirmLabel: M("閉じる"),
                    onConfirm: () =>
                    {
                        SettingsStore.SetString(PrefLastSeenVersion, currentVersion);
                    });
                rootVisualElement.Add(dialog);
            }
        }

        private static string FindProjectManager()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var alcom = System.IO.Path.Combine(localAppData, "ALCOM", "ALCOM.exe");
            if (System.IO.File.Exists(alcom)) return alcom;

            var vcc = System.IO.Path.Combine(localAppData, "Programs",
                "VRChat Creator Companion", "CreatorCompanion.exe");
            if (System.IO.File.Exists(vcc)) return vcc;

            return null;
        }
    }
}
