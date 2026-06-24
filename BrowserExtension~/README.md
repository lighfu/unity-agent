# Unity Agent - Web Browser Bridge

Chrome extension for connecting supported web AI chat pages to a local
UnityAgent instance over `ws://127.0.0.1`.

## Icon assets

`manifest.json` references these generated icon files:

- `icons/icon-16.png`
- `icons/icon-48.png`
- `icons/icon-128.png`

They are intentionally not committed because repository-wide PNG files are
ignored. Chrome reads these paths directly from `manifest.json`; if the files
are missing, loading or packaging the extension can report missing icon assets
or fall back to a default icon. Before loading or packaging the extension,
generate them locally:

```sh
python generate_icons.py
```

The script creates the `icons/` directory and writes the three PNG files used
by Chrome.
