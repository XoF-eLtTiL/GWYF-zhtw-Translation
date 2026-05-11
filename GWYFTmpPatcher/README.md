# GWYF TMP Patcher

This BepInEx plugin patches the GWYF Traditional Chinese experience.

Included behavior:

- replaces only the game's `ChineseFont` TMP font with the embedded `notosanstc-medium sdf` TMP font AssetBundle
- applies XUnity translation files to the known missed TMP item-name target
- replaces the built-in `GWYF_LOGO_1280x720` logo texture with the embedded `GWYFTW_LOGO.png`

Embedded assets:

- `Assets/notosanstc-medium sdf`
- `Assets/GWYFTW_LOGO.png`

The patcher does not download or execute assets. The embedded font AssetBundle is loaded from DLL memory, and the embedded PNG is decoded as a texture.
