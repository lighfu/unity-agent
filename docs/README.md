# UnityAgent — Public Site

[日本語](#日本語) | [English](#english) | [繁體中文](#繁體中文) | [简体中文](#简体中文)

## 日本語

このディレクトリは UnityAgent の公開ランディングサイトを配置しています。

- https://lighfu.github.io/unity-agent/

### 技術スタック

Static HTML / CSS / JS。ビルド手順はありません。

```
docs/
├── index.html        ランディング (single page, ja/en/zh-TW/zh)
├── css/style.css
├── js/
│   ├── i18n.js       data-i18n based ja/en/zh-TW/zh switcher
│   ├── data.js       tool categories / providers / changelog
│   └── main.js       theme, tabs, dynamic rendering
├── assets/           ロゴ・スクリーンショット
└── .nojekyll         disables Jekyll on GitHub Pages
```

### ローカルプレビュー

```sh
cd docs
python -m http.server 8000
# open http://localhost:8000/
```

### GitHub Pages

Repository → Settings → Pages

- Source: **Deploy from a branch**
- Branch: **master**, folder: **/docs**

`.nojekyll` により、静的ファイルがそのまま配信されます。

### コンテンツ更新

| 内容 | 場所 |
| --- | --- |
| Hero / sections | `index.html` (HTML structure) |
| Translation strings | `js/i18n.js` (`STRINGS.ja` / `STRINGS.en` / `STRINGS["zh-TW"]` / `STRINGS.zh`) |
| Tool categories | `js/data.js` (`TOOL_CATEGORIES`) |
| Provider list | `js/data.js` (`PROVIDERS`) |
| Changelog excerpt | `js/data.js` (`CHANGELOG`) |
| Brand colors | `css/style.css` (`:root` variables) |

### ロゴ / ブランド素材

`assets/mark*.svg` と `assets/wordmark-*.svg` は正規の `UnityAgent-logo-pack` から反映されています。マスターパックを更新する場合は、`mark-*.svg` と `unityagent-*.svg` をコピーし、ファイル名が変わった場合は参照も調整してください。

## English

This directory hosts the public landing site for UnityAgent.

- https://lighfu.github.io/unity-agent/

### Stack

Static HTML / CSS / JS. No build step.

```
docs/
├── index.html        Landing page (single page, ja/en/zh-TW/zh)
├── css/style.css
├── js/
│   ├── i18n.js       data-i18n based ja/en/zh-TW/zh switcher
│   ├── data.js       tool categories / providers / changelog
│   └── main.js       theme, tabs, dynamic rendering
├── assets/           logos and screenshots
└── .nojekyll         disables Jekyll on GitHub Pages
```

### Local preview

```sh
cd docs
python -m http.server 8000
# open http://localhost:8000/
```

### GitHub Pages

Repository → Settings → Pages

- Source: **Deploy from a branch**
- Branch: **master**, folder: **/docs**

The `.nojekyll` file ensures static files load as-is.

### Update content

| What | Where |
| --- | --- |
| Hero / sections | `index.html` (HTML structure) |
| Translation strings | `js/i18n.js` (`STRINGS.ja` / `STRINGS.en` / `STRINGS["zh-TW"]` / `STRINGS.zh`) |
| Tool categories | `js/data.js` (`TOOL_CATEGORIES`) |
| Provider list | `js/data.js` (`PROVIDERS`) |
| Changelog excerpt | `js/data.js` (`CHANGELOG`) |
| Brand colors | `css/style.css` (`:root` variables) |

### Logo / brand assets

`assets/mark*.svg` and `assets/wordmark-*.svg` are mirrored from the canonical `UnityAgent-logo-pack`. When updating the master pack, copy `mark-*.svg` and `unityagent-*.svg` over and adjust the references if filenames change.

## 繁體中文

此目錄放置 UnityAgent 的公開落地頁。

- https://lighfu.github.io/unity-agent/

### 技術堆疊

Static HTML / CSS / JS。沒有建置步驟。

```
docs/
├── index.html        落地頁 (single page, ja/en/zh-TW/zh)
├── css/style.css
├── js/
│   ├── i18n.js       data-i18n based ja/en/zh-TW/zh switcher
│   ├── data.js       tool categories / providers / changelog
│   └── main.js       theme, tabs, dynamic rendering
├── assets/           logo 與截圖
└── .nojekyll         disables Jekyll on GitHub Pages
```

### 本機預覽

```sh
cd docs
python -m http.server 8000
# open http://localhost:8000/
```

### GitHub Pages

Repository → Settings → Pages

- Source: **Deploy from a branch**
- Branch: **master**, folder: **/docs**

`.nojekyll` 檔案會確保靜態檔案依原樣載入。

### 更新內容

| 內容 | 位置 |
| --- | --- |
| Hero / sections | `index.html` (HTML structure) |
| Translation strings | `js/i18n.js` (`STRINGS.ja` / `STRINGS.en` / `STRINGS["zh-TW"]` / `STRINGS.zh`) |
| Tool categories | `js/data.js` (`TOOL_CATEGORIES`) |
| Provider list | `js/data.js` (`PROVIDERS`) |
| Changelog excerpt | `js/data.js` (`CHANGELOG`) |
| Brand colors | `css/style.css` (`:root` variables) |

### Logo / 品牌素材

`assets/mark*.svg` 與 `assets/wordmark-*.svg` 由正式的 `UnityAgent-logo-pack` 同步而來。更新 master pack 時，請複製 `mark-*.svg` 與 `unityagent-*.svg`，若檔名變更也請同步調整引用。

## 简体中文

此目录存放 UnityAgent 的公开落地页。

- https://lighfu.github.io/unity-agent/

### 技术栈

Static HTML / CSS / JS。没有构建步骤。

```
docs/
├── index.html        落地页 (single page, ja/en/zh-TW/zh)
├── css/style.css
├── js/
│   ├── i18n.js       data-i18n based ja/en/zh-TW/zh switcher
│   ├── data.js       tool categories / providers / changelog
│   └── main.js       theme, tabs, dynamic rendering
├── assets/           logo 与截图
└── .nojekyll         disables Jekyll on GitHub Pages
```

### 本地预览

```sh
cd docs
python -m http.server 8000
# open http://localhost:8000/
```

### GitHub Pages

Repository → Settings → Pages

- Source: **Deploy from a branch**
- Branch: **master**, folder: **/docs**

`.nojekyll` 文件会确保静态文件按原样加载。

### 更新内容

| 内容 | 位置 |
| --- | --- |
| Hero / sections | `index.html` (HTML structure) |
| Translation strings | `js/i18n.js` (`STRINGS.ja` / `STRINGS.en` / `STRINGS["zh-TW"]` / `STRINGS.zh`) |
| Tool categories | `js/data.js` (`TOOL_CATEGORIES`) |
| Provider list | `js/data.js` (`PROVIDERS`) |
| Changelog excerpt | `js/data.js` (`CHANGELOG`) |
| Brand colors | `css/style.css` (`:root` variables) |

### Logo / 品牌素材

`assets/mark*.svg` 和 `assets/wordmark-*.svg` 由正式的 `UnityAgent-logo-pack` 同步而来。更新 master pack 时，请复制 `mark-*.svg` 和 `unityagent-*.svg`，如果文件名发生变化，也请同步调整引用。
