# UnityAgent — Public Site

This directory hosts the public landing site for UnityAgent at:

- https://lighfu.github.io/unity-agent/

## Stack

Static HTML / CSS / JS. No build step.

```
docs/
├── index.html        ランディング (single page, ja/en bilingual)
├── css/style.css
├── js/
│   ├── i18n.js       data-i18n based ja/en switcher
│   ├── data.js       tool categories / providers / changelog
│   └── main.js       theme, tabs, dynamic rendering
├── assets/           ロゴ・スクリーンショット
└── .nojekyll         disables Jekyll on GitHub Pages
```

## Local preview

```sh
cd docs
python -m http.server 8000
# open http://localhost:8000/
```

## GitHub Pages

Repository → Settings → Pages
- Source: **Deploy from a branch**
- Branch: **master**, folder: **/docs**

The `.nojekyll` file ensures static files load as-is.

## Update content

| What | Where |
| --- | --- |
| Hero / sections | `index.html` (HTML structure) |
| Translation strings | `js/i18n.js` (`STRINGS.ja` / `STRINGS.en`) |
| Tool categories | `js/data.js` (`TOOL_CATEGORIES`) |
| Provider list | `js/data.js` (`PROVIDERS`) |
| Changelog excerpt | `js/data.js` (`CHANGELOG`) |
| Brand colors | `css/style.css` (`:root` variables) |

## Logo / brand assets

`assets/mark*.svg` and `assets/wordmark-*.svg` are mirrored from the canonical
`UnityAgent-logo-pack`. When updating the master pack, copy `mark-*.svg` and
`unityagent-*.svg` over and adjust the references if filenames change.
