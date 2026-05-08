# GWYF TMP Patcher

This plugin combines the useful parts of the old TMP font and text replacer plugins.

It does not load external fonts. Its font-side job is only to correct overly thick outlines on the game's original TMP materials.

It also applies XUnity translation files to text objects missed by XUnity hooks:

- `BepInEx/Translation/zh-TW/Text/Complate.txt`
- `BepInEx/Translation/zh-TW/Text/_Postprocessors.txt`

Default behavior:

- Fix TMP outline width to `0.018`
- Only adjust TMP objects whose text contains CJK characters
- Apply `FaceDilate = -0.03` to make overly heavy glyphs slightly thinner
- Translate missed `TMP_Text` objects only
- Do not scan legacy `UnityEngine.UI.Text` / `TextMesh` unless enabled in config
