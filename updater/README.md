# GWYF Translation Updater

This BepInEx plugin syncs translation data from the raw GitHub translation repository for `GWYF-zhtw-Translation`.

## Remote layout

- `manifest.txt`
- `translations/zh-TW/Text/...`
- `translations/zh-TW/Texture/...`

## Locked remote

- `https://raw.githubusercontent.com/XoF-eLtTiL/GWYF-zhtw-Translation/main/manifest.txt`
- `https://raw.githubusercontent.com/XoF-eLtTiL/GWYF-zhtw-Translation/main/translations`

The remote source is hardcoded on purpose. It cannot be changed through config.

## Safety allowlist

The updater only downloads files listed in `manifest.txt` when they match all of these rules:

- path starts with `zh-TW/Text/` or `zh-TW/Texture/`
- extension is `.txt` or `.png`
- path is relative and does not contain `..`
- sha256 in the manifest matches the downloaded bytes

Downloaded files are only written to `BepInEx/Translation`. The updater never loads or executes downloaded content.

## Manual commands

Edit:

- `BepInEx/config/GWYF.TranslationUpdater/commands.txt`

Commands:

- `update`
- `force`
- `status`
