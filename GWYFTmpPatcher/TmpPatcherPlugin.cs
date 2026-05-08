using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GWYFTmpPatcher;

[BepInPlugin("codex.gwyf.tmppatcher", "GWYF TMP Patcher", "0.1.6")]
public sealed class TmpPatcherPlugin : BaseUnityPlugin
{
    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<float> _scanIntervalSeconds = null!;
    private ConfigEntry<bool> _patchOnlyUi = null!;
    private ConfigEntry<bool> _fixTmpOutline = null!;
    private ConfigEntry<bool> _onlyPatchCjkText = null!;
    private ConfigEntry<string> _outlineTargetPaths = null!;
    private ConfigEntry<float> _outlineWidth = null!;
    private ConfigEntry<float> _outlineAlpha = null!;
    private ConfigEntry<float> _faceDilate = null!;
    private ConfigEntry<bool> _forceOutlineColor = null!;
    private ConfigEntry<bool> _translateTmpText = null!;
    private ConfigEntry<bool> _translateUnityUiText = null!;
    private ConfigEntry<bool> _translateTextMesh = null!;
    private ConfigEntry<string> _translationTargetPaths = null!;
    private ConfigEntry<bool> _forceMeshUpdate = null!;
    private ConfigEntry<bool> _logChanges = null!;

    private const string OutlineMaterialPrefix = "GWYF TMP Outline ";

    private static readonly int FaceDilateId = Shader.PropertyToID("_FaceDilate");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

    private readonly Dictionary<int, Material> _outlineMaterials = new Dictionary<int, Material>();
    private readonly Dictionary<string, string> _exactRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _postRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<RegexRule> _regexRules = new List<RegexRule>();
    private readonly Dictionary<int, string> _lastAppliedText = new Dictionary<int, string>();
    private readonly Dictionary<string, DateTime> _translationFileTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

    private string _translationTextDirectory = null!;
    private float _nextScanAt;

    private void Awake()
    {
        _enabled = Config.Bind("General", "Enabled", true, "Enable TMP patching.");
        _scanIntervalSeconds = Config.Bind("General", "ScanIntervalSeconds", 0.05f, "How often to scan text objects.");
        _patchOnlyUi = Config.Bind("General", "PatchOnlyUi", false, "Only patch UI text under Canvas/TextMeshProUGUI.");
        _fixTmpOutline = Config.Bind("Outline", "FixTmpOutline", true, "Fix overly thick outlines on original TMP materials.");
        _onlyPatchCjkText = Config.Bind("Outline", "OnlyPatchCjkText", false, "Only adjust outline materials on TMP text that contains CJK characters.");
        _outlineTargetPaths = Config.Bind(
            "Outline",
            "OutlineTargetPaths",
            string.Empty,
            "Only apply TMP font/outline repair to these hierarchy paths. Separate multiple paths with |, comma, semicolon, or new lines. Empty means all UI text.");
        _outlineWidth = Config.Bind("Outline", "OutlineWidth", 0.06f, "Forced TMP outline width. Use -1 to keep original width.");
        _outlineAlpha = Config.Bind("Outline", "OutlineAlpha", 1f, "Outline alpha when OutlineWidth is applied.");
        _faceDilate = Config.Bind("Outline", "FaceDilate", 0f, "TMP face dilate. Negative values make glyphs slightly thinner. Use -999 to preserve original.");
        _forceOutlineColor = Config.Bind("Outline", "ForceOutlineColor", true, "Force TMP outline color to black so the repaired outline stays visible.");
        _translateTmpText = Config.Bind("Translation", "TranslateTmpText", true, "Apply XUnity translation files to TMP_Text objects missed by XUnity hooks.");
        _translateUnityUiText = Config.Bind("Translation", "TranslateUnityUiText", false, "Apply XUnity translation files to legacy UnityEngine.UI.Text objects.");
        _translateTextMesh = Config.Bind("Translation", "TranslateTextMesh", false, "Apply XUnity translation files to legacy UnityEngine.TextMesh objects.");
        _translationTargetPaths = Config.Bind(
            "Translation",
            "TranslationTargetPaths",
            "/LocalManager/UI/InteractionUIPanel/TextPanel/ItemNameText",
            "Only apply replacer translations to these hierarchy paths. Separate multiple paths with |, comma, semicolon, or new lines. Empty means all UI text.");
        _forceMeshUpdate = Config.Bind("General", "ForceMeshUpdate", true, "Force TMP meshes to redraw after patching.");
        _logChanges = Config.Bind("Diagnostics", "LogChanges", true, "Log applied outline and translation changes.");

        _translationTextDirectory = Path.Combine(Paths.GameRootPath, "BepInEx", "Translation", "zh-TW", "Text");
        ReloadTranslationsIfNeeded(force: true);

        SceneManager.sceneLoaded += OnSceneLoaded;
        PatchLoadedObjects("startup");
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        if (!_enabled.Value || Time.unscaledTime < _nextScanAt)
        {
            return;
        }

        _nextScanAt = Time.unscaledTime + Mathf.Max(0.02f, _scanIntervalSeconds.Value);
        ReloadTranslationsIfNeeded(force: false);
        PatchLoadedObjects("poll");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!_enabled.Value)
        {
            return;
        }

        ReloadTranslationsIfNeeded(force: false);
        PatchLoadedObjects($"scene:{scene.name}");
    }

    private void PatchLoadedObjects(string reason)
    {
        int outlined = 0;
        int translated = 0;

        foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (text == null || IsEditorOrAssetObject(text))
            {
                continue;
            }

            bool changed = false;
            if (_translateTmpText.Value && ShouldPatchTextComponent(text) && ShouldTranslateComponent(text) &&
                TryTranslateText(text, text.text, value => text.text = value, reason, "TMP"))
            {
                changed = true;
                translated++;
            }

            if (_fixTmpOutline.Value && text.fontSharedMaterial != null && ShouldPatchOutline(text))
            {
                Material material = GetOutlineAdjustedMaterial(text.fontSharedMaterial);
                if (!ReferenceEquals(text.fontSharedMaterial, material))
                {
                    text.fontSharedMaterial = material;
                    changed = true;
                    outlined++;
                }
            }

            if (changed && _forceMeshUpdate.Value)
            {
                text.SetAllDirty();
                text.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
            }
        }

        if (_translateUnityUiText.Value)
        {
            foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (text != null && !IsEditorOrAssetObject(text) && ShouldPatchTextComponent(text) && ShouldTranslateComponent(text) &&
                    TryTranslateText(text, text.text, value => text.text = value, reason, "UI.Text"))
                {
                    translated++;
                }
            }
        }

        if (_translateTextMesh.Value)
        {
            foreach (TextMesh text in Resources.FindObjectsOfTypeAll<TextMesh>())
            {
                if (text != null && !IsEditorOrAssetObject(text) && ShouldPatchTextComponent(text) && ShouldTranslateComponent(text) &&
                    TryTranslateText(text, text.text, value => text.text = value, reason, "TextMesh"))
                {
                    translated++;
                }
            }
        }

        if (_logChanges.Value && (outlined > 0 || translated > 0))
        {
            Logger.LogInfo($"TMP patch complete ({reason}). Outline={outlined}, translated={translated}.");
        }
    }

    private Material GetOutlineAdjustedMaterial(Material originalMaterial)
    {
        if (originalMaterial.name.StartsWith(OutlineMaterialPrefix, StringComparison.Ordinal))
        {
            ApplyOutlinePolicy(originalMaterial);
            return originalMaterial;
        }

        int key = originalMaterial.GetInstanceID();
        if (_outlineMaterials.TryGetValue(key, out Material cachedMaterial))
        {
            ApplyOutlinePolicy(cachedMaterial);
            return cachedMaterial;
        }

        Material material = new Material(originalMaterial)
        {
            name = $"{OutlineMaterialPrefix}{originalMaterial.name}"
        };
        ApplyOutlinePolicy(material);
        _outlineMaterials[key] = material;
        return material;
    }

    private void ApplyOutlinePolicy(Material material)
    {
        if (material.HasProperty(FaceDilateId) && _faceDilate.Value > -999f)
        {
            material.SetFloat(FaceDilateId, Mathf.Clamp(_faceDilate.Value, -1f, 1f));
        }

        if (!material.HasProperty(OutlineWidthId))
        {
            return;
        }

        float width = _outlineWidth.Value;
        if (width >= 0f)
        {
            material.SetFloat(OutlineWidthId, Mathf.Clamp01(width));
            material.EnableKeyword("OUTLINE_ON");

            if (material.HasProperty(OutlineColorId))
            {
                Color color = _forceOutlineColor.Value ? Color.black : material.GetColor(OutlineColorId);
                color.a = Mathf.Clamp01(_outlineAlpha.Value);
                material.SetColor(OutlineColorId, color);
            }
        }
    }

    private bool ShouldPatchOutline(TMP_Text text)
    {
        if (_onlyPatchCjkText.Value && !ContainsCjk(text.text))
        {
            return false;
        }

        string targets = _outlineTargetPaths.Value;
        if (string.IsNullOrWhiteSpace(targets))
        {
            return true;
        }

        return MatchesAnyTargetPath(text.transform, targets);
    }

    private bool ShouldTranslateComponent(Component component)
    {
        string targets = _translationTargetPaths.Value;
        if (string.IsNullOrWhiteSpace(targets))
        {
            return true;
        }

        return MatchesAnyTargetPath(component.transform, targets);
    }

    private bool ShouldPatchTextComponent(Component component)
    {
        if (!_patchOnlyUi.Value)
        {
            return true;
        }

        return component is TextMeshProUGUI ||
            component is Text ||
            component.GetComponentInParent<Canvas>(includeInactive: true) != null;
    }

    private static IEnumerable<string> SplitTargets(string value)
    {
        return value.Split(new[] { '|', ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0);
    }

    private static bool MatchesAnyTargetPath(Transform transform, string targets)
    {
        string path = GetHierarchyPath(transform);
        foreach (string target in SplitTargets(targets))
        {
            string normalizedTarget = NormalizeHierarchyPath(target);
            if (string.Equals(path, normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(normalizedTarget + "/", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

    private static bool ContainsCjk(string value)
    {
        foreach (char ch in value)
        {
            if (IsCjk(ch))
            {
                return true;
            }
        }

        return false;
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
