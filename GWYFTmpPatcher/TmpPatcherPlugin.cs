using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GWYFTmpPatcher;

[BepInPlugin("codex.gwyf.tmppatcher", "GWYF TMP Patcher", "0.1.14")]
public sealed class TmpPatcherPlugin : BaseUnityPlugin
{
    private sealed class TextureReplacement
    {
        public string Name { get; set; } = string.Empty;
        public Texture2D Texture { get; set; } = null!;
        public HashSet<int> PatchedTextures { get; } = new HashSet<int>();
        public Dictionary<int, Sprite> SpriteCache { get; } = new Dictionary<int, Sprite>();
    }

    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<bool> _patchOnlyUi = null!;
    private ConfigEntry<bool> _translateTmpText = null!;
    private ConfigEntry<bool> _forceMeshUpdate = null!;
    private ConfigEntry<bool> _logChanges = null!;
    private ConfigEntry<int> _fontBatchSize = null!;
    private ConfigEntry<bool> _replaceFontWithExternalFile = null!;
    private ConfigEntry<string> _externalFontPath = null!;
    private ConfigEntry<string> _assetBundleFontAssetName = null!;
    private ConfigEntry<bool> _replaceBuiltInTextures = null!;
    private ConfigEntry<int> _textureBatchSize = null!;

    private const string TargetTmpFontName = "ChineseFont";
    private const string LogoTextureName = "GWYF_LOGO_1280x720";
    private const string ReplacerTargetPath = "/LocalManager/UI/InteractionUIPanel/TextPanel/ItemNameText";
    private const string EmbeddedFontResourceName = "GWYFTmpPatcher.Assets.notosanstc-medium-sdf.bundle";
    private const string EmbeddedLogoResourceName = "GWYFTmpPatcher.Assets.GWYFTW_LOGO.png";
    private static readonly string[] TmpFontAssetCopyFields =
    {
        "m_Version",
        "m_FaceInfo",
        "m_GlyphTable",
        "m_CharacterTable",
        "m_AtlasTexture",
        "m_AtlasTextures",
        "m_AtlasTextureIndex",
        "m_UsedGlyphRects",
        "m_FreeGlyphRects",
        "m_GlyphIndexList",
        "m_GlyphIndexListNewlyAdded",
        "m_GlyphsToRender",
        "m_GlyphsRendered",
        "m_AtlasWidth",
        "m_AtlasHeight",
        "m_AtlasPadding",
        "m_AtlasRenderMode",
        "m_AtlasPopulationMode",
        "m_FontFeatureTable",
        "m_fontInfo",
        "m_glyphInfoList",
        "m_KerningTable",
        "m_kerningInfo",
        "fallbackFontAssets"
    };

    private readonly HashSet<int> _patchedFontAssetIds = new HashSet<int>();
    private readonly List<AssetBundle> _loadedFontBundles = new List<AssetBundle>();
    private readonly Dictionary<string, TextureReplacement> _textureReplacements = new Dictionary<string, TextureReplacement>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _exactRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _postRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<RegexRule> _regexRules = new List<RegexRule>();
    private readonly Dictionary<int, string> _lastAppliedText = new Dictionary<int, string>();
    private readonly Dictionary<string, DateTime> _translationFileTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

    private string _translationTextDirectory = null!;
    private TMP_FontAsset? _externalFontAsset;
    private bool _embeddedFontLoadAttempted;
    private bool _textureReplacementsLoaded;
    private static readonly MethodInfo? LoadImageMethod = Type
        .GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
        ?.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }, null);

    private void Awake()
    {
        _enabled = Config.Bind("General", "Enabled", true, "Enable TMP patching.");
        _patchOnlyUi = Config.Bind("General", "PatchOnlyUi", true, "Only patch UI text under Canvas/TextMeshProUGUI.");
        _translateTmpText = Config.Bind("Translation", "TranslateTmpText", true, "Apply XUnity translation files to TMP_Text objects missed by XUnity hooks.");
        _forceMeshUpdate = Config.Bind("General", "ForceMeshUpdate", false, "Force TMP meshes to redraw after text replacement.");
        _logChanges = Config.Bind("Diagnostics", "LogChanges", true, "Log applied font and translation changes.");
        _fontBatchSize = Config.Bind("Performance", "FontBatchSize", 32, "How many TMP objects to font patch per frame during scene scans.");
        _replaceFontWithExternalFile = Config.Bind("Font", "ReplaceFontWithExternalFile", true, "Patch only the game's ChineseFont TMP font asset.");
        _externalFontPath = Config.Bind("Font", "ExternalFontPath", "notosanstc-medium sdf", "External TMP font AssetBundle path. Relative paths are searched from the game root and BepInEx/config.");
        _assetBundleFontAssetName = Config.Bind("Font", "AssetBundleFontAssetName", "", "TMP_FontAsset name inside the AssetBundle. Leave blank to use the first TMP_FontAsset.");
        _replaceBuiltInTextures = Config.Bind("Texture", "ReplaceBuiltInTextures", true, "Replace selected game textures from resources embedded in this patcher.");
        _textureBatchSize = Config.Bind("Performance", "TextureBatchSize", 64, "How many texture related objects to inspect per frame during texture replacement scans.");

        _translationTextDirectory = Path.Combine(Paths.GameRootPath, "BepInEx", "Translation", "zh-TW", "Text");
        ReloadTranslationsIfNeeded(force: true);
        TryLoadExternalFont();
        LoadEmbeddedTextureReplacements();

        SceneManager.sceneLoaded += OnSceneLoaded;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        QueueSceneFontScans("startup");
        QueueTextureScans("startup");
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        foreach (AssetBundle bundle in _loadedFontBundles)
        {
            bundle.Unload(unloadAllLoadedObjects: false);
        }

        _loadedFontBundles.Clear();
        foreach (TextureReplacement replacement in _textureReplacements.Values.Distinct())
        {
            if (replacement.Texture != null)
            {
                Destroy(replacement.Texture);
            }
        }

        _textureReplacements.Clear();
    }

    private void OnTextChanged(UnityEngine.Object changedObject)
    {
        if (!_enabled.Value || changedObject is not TMP_Text text || text == null || IsEditorOrAssetObject(text))
        {
            return;
        }

        ReloadTranslationsIfNeeded(force: false);
        PatchTmpText(text, "text-changed", allowTranslate: true, allowFont: true, out _, out bool textTranslated);
        if (textTranslated && _forceMeshUpdate.Value)
        {
            text.SetAllDirty();
            text.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_enabled.Value)
        {
            return;
        }

        ReloadTranslationsIfNeeded(force: false);
        QueueSceneFontScans($"scene:{scene.name}");
        QueueTextureScans($"scene:{scene.name}");
    }

    private void QueueSceneFontScans(string reason)
    {
        if (!_replaceFontWithExternalFile.Value)
        {
            return;
        }

        StartCoroutine(PatchSceneTmpObjectsBatched(reason, 0f));
        StartCoroutine(PatchSceneTmpObjectsBatched(reason + ":delayed1", 1f));
        StartCoroutine(PatchSceneTmpObjectsBatched(reason + ":delayed3", 3f));
    }

    private IEnumerator PatchSceneTmpObjectsBatched(string reason, float delaySeconds)
    {
        if (delaySeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
        }

        TMP_Text[] texts = Resources.FindObjectsOfTypeAll<TMP_Text>();
        HashSet<int> seen = new HashSet<int>();
        int fontPatched = 0;
        int translated = 0;
        int processedThisFrame = 0;
        int batchSize = Mathf.Max(1, _fontBatchSize.Value);

        foreach (TMP_FontAsset fontAsset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (fontAsset != null && PatchChineseFontAsset(fontAsset))
            {
                fontPatched++;
            }

            if (++processedThisFrame >= batchSize)
            {
                processedThisFrame = 0;
                yield return null;
            }
        }

        foreach (TMP_Text text in texts)
        {
            if (text == null || IsEditorOrAssetObject(text) || !seen.Add(text.GetInstanceID()))
            {
                continue;
            }

            PatchTmpText(text, reason, allowTranslate: false, allowFont: true, out bool fontChanged, out bool textTranslated);
            fontPatched += fontChanged ? 1 : 0;
            translated += textTranslated ? 1 : 0;

            if (textTranslated && _forceMeshUpdate.Value)
            {
                text.SetAllDirty();
                text.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
            }

            processedThisFrame++;
            if (processedThisFrame >= batchSize)
            {
                processedThisFrame = 0;
                yield return null;
            }
        }

        LogPatchSummary(reason, fontPatched, translated);
    }

    private void QueueTextureScans(string reason)
    {
        if (!_replaceBuiltInTextures.Value)
        {
            return;
        }

        StartCoroutine(PatchSceneTexturesBatched(reason, 0f));
        StartCoroutine(PatchSceneTexturesBatched(reason + ":delayed1", 1f));
        StartCoroutine(PatchSceneTexturesBatched(reason + ":delayed3", 3f));
    }

    private IEnumerator PatchSceneTexturesBatched(string reason, float delaySeconds)
    {
        if (delaySeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
        }

        if (!LoadEmbeddedTextureReplacements())
        {
            yield break;
        }

        int textures = 0;
        int images = 0;
        int spriteRenderers = 0;
        int materials = 0;
        int processedThisFrame = 0;
        int batchSize = Mathf.Max(1, _textureBatchSize.Value);

        foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>())
        {
            if (texture != null && PatchLoadedTextureObject(texture))
            {
                textures++;
            }

            if (++processedThisFrame >= batchSize)
            {
                processedThisFrame = 0;
                yield return null;
            }
        }

        foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (image != null && image.sprite != null && PatchImage(image))
            {
                images++;
            }

            if (++processedThisFrame >= batchSize)
            {
                processedThisFrame = 0;
                yield return null;
            }
        }

        foreach (SpriteRenderer renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (renderer != null && renderer.sprite != null && PatchSpriteRenderer(renderer))
            {
                spriteRenderers++;
            }

            if (++processedThisFrame >= batchSize)
            {
                processedThisFrame = 0;
                yield return null;
            }
        }

        foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
        {
            if (renderer != null)
            {
                materials += PatchRendererMaterials(renderer);
            }

            if (++processedThisFrame >= batchSize)
            {
                processedThisFrame = 0;
                yield return null;
            }
        }

        if (_logChanges.Value && (textures > 0 || images > 0 || spriteRenderers > 0 || materials > 0))
        {
            Logger.LogInfo($"Texture patch complete ({reason}). Textures={textures}, images={images}, spriteRenderers={spriteRenderers}, materials={materials}.");
        }
    }

    private bool PatchTmpText(TMP_Text text, string reason, bool allowTranslate, bool allowFont, out bool fontChanged, out bool textTranslated)
    {
        fontChanged = false;
        textTranslated = false;

        bool changed = false;
        if (allowTranslate && _translateTmpText.Value && ShouldPatchTextComponent(text) && IsReplacerTarget(text) &&
            TryTranslateText(text, text.text, value => text.text = value, reason, "TMP"))
        {
            changed = true;
            textTranslated = true;
        }

        if (allowFont && _replaceFontWithExternalFile.Value)
        {
            TryLoadExternalFont();
            if (PatchChineseFontAsset(text.font))
            {
                changed = true;
                fontChanged = true;
            }
        }

        return changed;
    }

    private bool PatchChineseFontAsset(TMP_FontAsset? targetFont)
    {
        TMP_FontAsset? replacementFont = _externalFontAsset;
        if (!_replaceFontWithExternalFile.Value || replacementFont == null)
        {
            return false;
        }

        if (targetFont == null || !IsTargetTmpFontAsset(targetFont) || ReferenceEquals(targetFont, replacementFont))
        {
            return false;
        }

        TMP_FontAsset fontToPatch = targetFont;
        int fontId = fontToPatch.GetInstanceID();
        if (_patchedFontAssetIds.Contains(fontId))
        {
            return false;
        }

        PatchTmpFontAssetInPlace(fontToPatch, replacementFont);
        _patchedFontAssetIds.Add(fontId);
        return true;
    }

    private void LogPatchSummary(string reason, int fontPatched, int translated)
    {
        if (_logChanges.Value && (fontPatched > 0 || translated > 0))
        {
            Logger.LogInfo($"TMP patch complete ({reason}). Fonts={fontPatched}, translated={translated}.");
        }
    }

    private bool LoadEmbeddedTextureReplacements()
    {
        if (_textureReplacementsLoaded)
        {
            return _textureReplacements.Count > 0;
        }

        _textureReplacementsLoaded = true;
        if (!_replaceBuiltInTextures.Value)
        {
            return false;
        }

        byte[]? logoBytes = ReadEmbeddedResourceBytes(EmbeddedLogoResourceName);
        if (logoBytes == null || logoBytes.Length == 0)
        {
            Logger.LogWarning("Embedded logo PNG was not found in patcher resources.");
            return false;
        }

        Texture2D logo = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
        if (!TryLoadPng(logo, logoBytes))
        {
            Destroy(logo);
            Logger.LogWarning("Embedded logo PNG could not be decoded.");
            return false;
        }

        logo.name = LogoTextureName;
        logo.wrapMode = TextureWrapMode.Clamp;
        logo.filterMode = FilterMode.Bilinear;

        TextureReplacement replacement = new TextureReplacement
        {
            Name = LogoTextureName,
            Texture = logo
        };

        _textureReplacements[LogoTextureName] = replacement;

        if (_logChanges.Value)
        {
            Logger.LogInfo($"Loaded embedded texture replacements: {_textureReplacements.Count} aliases, logo={logo.width}x{logo.height}");
        }

        return _textureReplacements.Count > 0;
    }

    private bool PatchLoadedTextureObject(Texture2D texture)
    {
        if (!TryGetTextureReplacement(texture.name, out TextureReplacement replacement))
        {
            return false;
        }

        int instanceId = texture.GetInstanceID();
        if (replacement.PatchedTextures.Contains(instanceId) ||
            ReferenceEquals(texture, replacement.Texture) ||
            texture.width != replacement.Texture.width ||
            texture.height != replacement.Texture.height)
        {
            return false;
        }

        try
        {
            Graphics.CopyTexture(replacement.Texture, texture);
            replacement.PatchedTextures.Add(instanceId);
            return true;
        }
        catch (Exception ex)
        {
            replacement.PatchedTextures.Add(instanceId);
            Logger.LogWarning($"Could not copy replacement into Texture2D '{texture.name}'; sprite/material replacement may still work. {ex.Message}");
            return false;
        }
    }

    private bool PatchImage(Image image)
    {
        TextureReplacement? replacement = FindReplacementForSprite(image.sprite);
        if (replacement == null)
        {
            return false;
        }

        Sprite sprite = GetReplacementSprite(replacement, image.sprite);
        if (ReferenceEquals(image.sprite, sprite))
        {
            return false;
        }

        image.sprite = sprite;
        image.SetAllDirty();
        return true;
    }

    private bool PatchSpriteRenderer(SpriteRenderer renderer)
    {
        TextureReplacement? replacement = FindReplacementForSprite(renderer.sprite);
        if (replacement == null)
        {
            return false;
        }

        Sprite sprite = GetReplacementSprite(replacement, renderer.sprite);
        if (ReferenceEquals(renderer.sprite, sprite))
        {
            return false;
        }

        renderer.sprite = sprite;
        return true;
    }

    private int PatchRendererMaterials(Renderer renderer)
    {
        int patched = 0;
        foreach (Material material in renderer.sharedMaterials ?? Array.Empty<Material>())
        {
            if (material == null || material.mainTexture == null)
            {
                continue;
            }

            if (TryGetTextureReplacement(material.mainTexture.name, out TextureReplacement replacement) &&
                !ReferenceEquals(material.mainTexture, replacement.Texture))
            {
                material.mainTexture = replacement.Texture;
                patched++;
            }
        }

        return patched;
    }

    private TextureReplacement? FindReplacementForSprite(Sprite sprite)
    {
        if (TryGetTextureReplacement(sprite.name, out TextureReplacement replacement))
        {
            return replacement;
        }

        Texture2D texture = sprite.texture;
        if (texture != null && TryGetTextureReplacement(texture.name, out replacement))
        {
            return replacement;
        }

        return null;
    }

    private bool TryGetTextureReplacement(string objectName, out TextureReplacement replacement)
    {
        if (_textureReplacements.TryGetValue(objectName, out replacement))
        {
            return true;
        }

        string normalizedName = NormalizeUnityObjectName(objectName);
        if (!string.Equals(normalizedName, objectName, StringComparison.Ordinal) &&
            _textureReplacements.TryGetValue(normalizedName, out replacement))
        {
            return true;
        }

        replacement = null!;
        return false;
    }

    private static string NormalizeUnityObjectName(string objectName)
    {
        string normalized = (objectName ?? string.Empty).Trim();
        foreach (string suffix in new[] { "(Clone)", "(Instance)" })
        {
            int index = normalized.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                normalized = normalized.Substring(0, index).Trim();
            }
        }

        return normalized;
    }

    private static Sprite GetReplacementSprite(TextureReplacement replacement, Sprite source)
    {
        int sourceId = source.GetInstanceID();
        if (replacement.SpriteCache.TryGetValue(sourceId, out Sprite cached))
        {
            return cached;
        }

        Rect rect = source.rect;
        if (rect.xMax > replacement.Texture.width || rect.yMax > replacement.Texture.height)
        {
            rect = new Rect(0f, 0f, replacement.Texture.width, replacement.Texture.height);
        }

        Vector2 pivot = new Vector2(0.5f, 0.5f);
        if (source.rect.width > 0f && source.rect.height > 0f)
        {
            pivot = new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height);
        }

        Sprite sprite = Sprite.Create(
            replacement.Texture,
            rect,
            pivot,
            source.pixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            source.border);
        sprite.name = replacement.Name;
        replacement.SpriteCache[sourceId] = sprite;
        return sprite;
    }

    private static IEnumerable<string> SplitAliases(string aliases)
    {
        return (aliases ?? string.Empty)
            .Split(new[] { '|', ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(alias => alias.Trim())
            .Where(alias => alias.Length > 0);
    }

    private static byte[]? ReadEmbeddedResourceBytes(string resourceName)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return null;
        }

        using MemoryStream memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static bool TryLoadPng(Texture2D texture, byte[] bytes)
    {
        if (LoadImageMethod == null)
        {
            return false;
        }

        object? result = LoadImageMethod.Invoke(null, new object[] { texture, bytes, false });
        return result is bool loaded && loaded;
    }

    private void TryLoadExternalFont()
    {
        if (!_replaceFontWithExternalFile.Value || _externalFontAsset != null)
        {
            return;
        }

        if (TryLoadEmbeddedFont())
        {
            return;
        }

        string? fontPath = ResolveExternalFontPath(_externalFontPath.Value);
        if (fontPath == null)
        {
            if (_logChanges.Value)
            {
                Logger.LogWarning($"External TMP font file was not found: {_externalFontPath.Value}");
            }

            return;
        }

        try
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(fontPath);
            if (bundle == null)
            {
                Logger.LogWarning($"External TMP font file is not a readable AssetBundle: {fontPath}");
                return;
            }

            _loadedFontBundles.Add(bundle);
            TryLoadFontFromBundle(bundle, fontPath);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to load external TMP font '{fontPath}': {ex.Message}");
        }
    }

    private bool TryLoadEmbeddedFont()
    {
        if (_embeddedFontLoadAttempted)
        {
            return _externalFontAsset != null;
        }

        _embeddedFontLoadAttempted = true;
        try
        {
            byte[]? bundleBytes = ReadEmbeddedResourceBytes(EmbeddedFontResourceName);
            if (bundleBytes == null || bundleBytes.Length == 0)
            {
                Logger.LogWarning("Embedded TMP font AssetBundle was not found in patcher resources.");
                return false;
            }

            AssetBundle bundle = AssetBundle.LoadFromMemory(bundleBytes);
            if (bundle == null)
            {
                Logger.LogWarning("Embedded TMP font AssetBundle could not be loaded.");
                return false;
            }

            _loadedFontBundles.Add(bundle);
            if (!TryLoadFontFromBundle(bundle, "embedded patcher resource"))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to load embedded TMP font AssetBundle: {ex.Message}");
            return false;
        }
    }

    private static string? ResolveExternalFontPath(string configuredPath)
    {
        string path = (configuredPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        string[] roots =
        {
            Paths.GameRootPath,
            Paths.ConfigPath,
            Path.Combine(Paths.ConfigPath, "TMPFonts"),
            Path.Combine(Paths.ConfigPath, "ConfigTMP"),
            Paths.PluginPath
        };

        foreach (string root in roots)
        {
            string candidate = Path.Combine(root, path);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static bool IsTargetTmpFontAsset(TMP_FontAsset? fontAsset)
    {
        return fontAsset != null &&
            string.Equals(NormalizeUnityObjectName(fontAsset.name), TargetTmpFontName, StringComparison.OrdinalIgnoreCase);
    }

    private void PatchTmpFontAssetInPlace(TMP_FontAsset targetFont, TMP_FontAsset sourceFont)
    {
        Material? targetMaterial = targetFont.material;
        Material? sourceMaterial = sourceFont.material;

        foreach (string fieldName in TmpFontAssetCopyFields)
        {
            CopyFieldValue(sourceFont, targetFont, fieldName);
        }

        if (targetMaterial != null && sourceMaterial != null)
        {
            string materialName = targetMaterial.name;
            targetMaterial.CopyPropertiesFromMaterial(sourceMaterial);
            targetMaterial.name = materialName;
            CopyFieldValue(sourceFont, targetFont, "m_Material", targetMaterial);
        }
        else if (sourceMaterial != null)
        {
            CopyFieldValue(sourceFont, targetFont, "m_Material");
        }

        RefreshTmpFontAsset(targetFont);
        if (_logChanges.Value)
        {
            Logger.LogInfo($"Patched TMP font asset '{TargetTmpFontName}' with '{sourceFont.name}'.");
        }
    }

    private static void CopyFieldValue(TMP_FontAsset source, TMP_FontAsset target, string fieldName)
    {
        FieldInfo? field = typeof(TMP_FontAsset).GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            return;
        }

        field.SetValue(target, field.GetValue(source));
    }

    private static void CopyFieldValue(TMP_FontAsset source, TMP_FontAsset target, string fieldName, object value)
    {
        FieldInfo? field = typeof(TMP_FontAsset).GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(target, value);
    }

    private static void RefreshTmpFontAsset(TMP_FontAsset fontAsset)
    {
        MethodInfo? readDefinition = typeof(TMP_FontAsset).GetMethod("ReadFontAssetDefinition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (readDefinition != null)
        {
            readDefinition.Invoke(fontAsset, null);
            return;
        }

        MethodInfo? initializeLookup = typeof(TMP_FontAsset).GetMethod("InitializeDictionaryLookupTables", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        initializeLookup?.Invoke(fontAsset, null);
    }

    private static bool IsReplacerTarget(TMP_Text text)
    {
        string path = GetHierarchyPath(text.transform);
        string target = NormalizeHierarchyPath(ReplacerTargetPath);
        return string.Equals(path, target, StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(target, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldPatchTextComponent(Component component)
    {
        if (!_patchOnlyUi.Value)
        {
            return true;
        }

        return component is TextMeshProUGUI ||
            component.GetComponentInParent<Canvas>(includeInactive: true) != null;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        Stack<string> names = new Stack<string>();
        for (Transform? current = transform; current != null; current = current.parent)
        {
            names.Push(current.name);
        }

        return "/" + string.Join("/", names.Select(NormalizePathSegment));
    }

    private static string NormalizeHierarchyPath(string path)
    {
        string normalized = path.Replace('\\', '/').Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = "/" + normalized;
        }

        return string.Join("/", normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizePathSegment)
            .Prepend(string.Empty));
    }

    private static string NormalizePathSegment(string segment)
    {
        int cloneIndex = segment.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
        if (cloneIndex >= 0)
        {
            segment = segment.Remove(cloneIndex);
        }

        return segment.Trim();
    }

    private bool TryTranslateText(Component component, string original, Action<string> setText, string reason, string kind)
    {
        if (string.IsNullOrWhiteSpace(original) || IsMostlyCjk(original))
        {
            return false;
        }

        int id = component.GetInstanceID();
        if (_lastAppliedText.TryGetValue(id, out string lastApplied) &&
            string.Equals(lastApplied, original, StringComparison.Ordinal))
        {
            return false;
        }

        string translated = Translate(original);
        if (string.Equals(translated, original, StringComparison.Ordinal))
        {
            return false;
        }

        setText(translated);
        _lastAppliedText[id] = translated;
        if (_logChanges.Value)
        {
            Logger.LogInfo($"{kind} translated ({reason}): {EscapeForLog(original)} -> {EscapeForLog(translated)}");
        }

        return true;
    }

    private string Translate(string input)
    {
        string normalized = NormalizeText(input);
        if (_exactRules.TryGetValue(normalized, out string exact))
        {
            return exact;
        }

        string output = input;
        foreach (RegexRule rule in _regexRules)
        {
            if (rule.Regex.IsMatch(output))
            {
                output = rule.Regex.Replace(output, rule.Replacement);
            }
        }

        foreach (KeyValuePair<string, string> rule in _postRules.OrderByDescending(rule => rule.Key.Length))
        {
            output = Regex.Replace(
                output,
                Regex.Escape(rule.Key),
                EscapeReplacement(rule.Value),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return output;
    }

    private void ReloadTranslationsIfNeeded(bool force)
    {
        if (!Directory.Exists(_translationTextDirectory))
        {
            return;
        }

        string[] paths =
        {
            Path.Combine(_translationTextDirectory, "Complate.txt"),
            Path.Combine(_translationTextDirectory, "_Postprocessors.txt")
        };

        bool changed = force;
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            DateTime writeTime = File.GetLastWriteTimeUtc(path);
            if (!_translationFileTimes.TryGetValue(path, out DateTime previous) || writeTime > previous)
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        _exactRules.Clear();
        _regexRules.Clear();
        _postRules.Clear();
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            LoadTranslationFile(path, isPostprocessor: Path.GetFileName(path).Equals("_Postprocessors.txt", StringComparison.OrdinalIgnoreCase));
            _translationFileTimes[path] = File.GetLastWriteTimeUtc(path);
        }

        Logger.LogInfo($"Loaded XUnity translations: exact={_exactRules.Count}, regex={_regexRules.Count}, post={_postRules.Count}");
    }

    private void LoadTranslationFile(string path, bool isPostprocessor)
    {
        foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string source = StripNoParse(line.Substring(0, separator).Trim());
            string target = StripNoParse(DecodeEscapes(line.Substring(separator + 1).Trim()));
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            if (source.StartsWith("r:\"", StringComparison.Ordinal) && source.EndsWith("\"", StringComparison.Ordinal))
            {
                string pattern = source.Substring(3, source.Length - 4);
                _regexRules.Add(new RegexRule(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), target));
            }
            else if (!isPostprocessor)
            {
                _exactRules[NormalizeText(source)] = target;
            }
            else if (IsSafePostprocessorSource(source))
            {
                _postRules[source] = target;
            }
        }
    }

    private bool TryLoadFontFromBundle(AssetBundle bundle, string source)
    {
        string configuredAssetName = _assetBundleFontAssetName.Value.Trim();
        TMP_FontAsset? fontAsset = null;
        if (!string.IsNullOrWhiteSpace(configuredAssetName))
            fontAsset = bundle.LoadAsset<TMP_FontAsset>(configuredAssetName);

        fontAsset ??= bundle.LoadAllAssets<TMP_FontAsset>().FirstOrDefault();
        if (fontAsset == null)
        {
            Logger.LogWarning($"No TMP_FontAsset found in AssetBundle: {source}");
            return false;
        }

        Texture2D? atlas = bundle.LoadAllAssets<Texture2D>().FirstOrDefault();
        if (atlas != null)
        {
            FieldInfo? atlasField = typeof(TMP_FontAsset)
                .GetField("m_AtlasTexture", BindingFlags.NonPublic | BindingFlags.Instance);
            atlasField?.SetValue(fontAsset, atlas);

            FieldInfo? atlasesField = typeof(TMP_FontAsset)
                .GetField("m_AtlasTextures", BindingFlags.NonPublic | BindingFlags.Instance);
            if (atlasesField?.GetValue(fontAsset) is Texture2D[] existing && existing.Length > 0)
                existing[0] = atlas;
            else
                atlasesField?.SetValue(fontAsset, new[] { atlas });

            if (fontAsset.material != null)
                fontAsset.material.SetTexture(ShaderUtilities.ID_MainTex, atlas);

            if (_logChanges.Value)
                Logger.LogInfo($"Bound atlas texture: {atlas.name} ({atlas.width}x{atlas.height})");
        }
        else
        {
            Logger.LogWarning($"No Texture2D found in AssetBundle: {source}; glyphs may render incorrectly.");
        }

        _externalFontAsset = fontAsset;
        Logger.LogInfo($"Loaded TMP font: {_externalFontAsset.name} from {source}");
        return true;
    }

    private static bool IsSafePostprocessorSource(string source)
    {
        if (source.Length < 3 || source.Any(ch => char.IsDigit(ch) || ch == '$'))
        {
            return false;
        }

        return source.Any(char.IsLetter);
    }

    private static string StripNoParse(string value)
    {
        return value
            .Replace("<noparse></noparse>", string.Empty)
            .Replace("<noparse><\\/noparse>", string.Empty)
            .Trim();
    }

    private static string NormalizeText(string value)
    {
        string normalized = StripNoParse(value)
            .Replace('\u00a0', ' ')
            .Replace('\u200b', ' ')
            .Replace('\u200c', ' ')
            .Replace('\u200d', ' ')
            .Replace("\\n", " ")
            .Replace("\r", " ")
            .Replace("\n", " ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static string DecodeEscapes(string value)
    {
        return value.Replace("\\n", Environment.NewLine).Replace("\\t", "\t");
    }

    private static string EscapeReplacement(string value)
    {
        return value.Replace("$", "$$");
    }

    private static string EscapeForLog(string value)
    {
        return value.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static bool IsEditorOrAssetObject(Component component)
    {
        GameObject gameObject = component.gameObject;
        return gameObject == null || !gameObject.scene.IsValid();
    }

    private static bool IsMostlyCjk(string value)
    {
        int cjk = 0;
        int letters = 0;
        foreach (char ch in value)
        {
            if (IsCjk(ch))
            {
                cjk++;
            }
            else if (char.IsLetter(ch))
            {
                letters++;
            }
        }

        return cjk > 0 && cjk >= letters;
    }

    private static bool IsCjk(char ch)
    {
        return (ch >= '\u4e00' && ch <= '\u9fff') ||
               (ch >= '\u3400' && ch <= '\u4dbf') ||
               (ch >= '\uf900' && ch <= '\ufaff');
    }

    private sealed class RegexRule
    {
        public RegexRule(Regex regex, string replacement)
        {
            Regex = regex;
            Replacement = replacement;
        }

        public Regex Regex { get; }
        public string Replacement { get; }
    }
}
