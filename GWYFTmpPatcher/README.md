# GWYF TMP Patcher

This BepInEx plugin patches the GWYF Traditional Chinese experience.

Included behavior:

- replaces TMP fonts with the embedded `notosanstc-medium sdf` TMP font AssetBundle
- applies XUnity translation files to the known missed TMP item-name target
- replaces selected logo textures with the embedded `GWYFTW_LOGO.png`

Embedded assets:

- `Assets/notosanstc-medium sdf`
- `Assets/GWYFTW_LOGO.png`

The patcher does not download or execute assets. The embedded font AssetBundle is loaded from DLL memory, and the embedded PNG is decoded as a texture.
