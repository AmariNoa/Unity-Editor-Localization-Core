using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace com.amari_noa.unity_editor_localization_core.editor
{
    public sealed class EditorLocalizationService : IEditorLocalizationService
    {
        private readonly Dictionary<string, EditorLocalizationSourceDefinition> _sources;
        private readonly EditorLocalizationStore _store;
        private readonly EditorLocalizationValidationService _validationService;

        private string _currentLanguageCode;
        private bool _isInitialized;
        private bool _missingSourceWarningLogged;

        public EditorLocalizationService()
        {
            _sources = new Dictionary<string, EditorLocalizationSourceDefinition>(StringComparer.Ordinal);
            _store = new EditorLocalizationStore(new EditorLocalizationJsonFlattener());
            _validationService = new EditorLocalizationValidationService(_store);
        }

        public string CurrentLanguageCode
        {
            get
            {
                EnsureInitialized();
                return _currentLanguageCode;
            }
        }

        public IReadOnlyList<string> RegisteredSourceIds
        {
            get
            {
                var result = new List<string>(_sources.Keys);
                result.Sort(StringComparer.Ordinal);
                return result;
            }
        }

        public event Action<string> LanguageChanged;

        public bool RegisterSource(EditorLocalizationSourceDefinition source)
        {
            EnsureInitialized();

            if (source == null || string.IsNullOrWhiteSpace(source.SourceId))
            {
                Debug.LogError($"{EditorLocalizationConstants.LogPrefix} RegisterSource failed: SourceId is empty.");
                return false;
            }

            if (_sources.ContainsKey(source.SourceId))
            {
                Debug.LogWarning($"{EditorLocalizationConstants.LogPrefix} Duplicate sourceId registration rejected: {source.SourceId}");
                return false;
            }

            var normalizedSource = NormalizeSourceDefinition(source);
            _sources[normalizedSource.SourceId] = normalizedSource;
            _store.InvalidateSource(normalizedSource.SourceId);
            _missingSourceWarningLogged = false;
            return true;
        }

        public bool UnregisterSource(string sourceId)
        {
            EnsureInitialized();

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return false;
            }

            if (!_sources.Remove(sourceId))
            {
                return false;
            }

            _store.InvalidateSource(sourceId);
            return true;
        }

        public EditorLocalizationSetLanguageResult SetLanguage(string sourceId, string languageCode)
        {
            EnsureInitialized();
            EnsureRegistered();

            if (!TryGetSource(sourceId, out var source))
            {
                ShowInvalidSourceCallDialog(sourceId);
                Debug.LogError($"{EditorLocalizationConstants.LogPrefix} SetLanguage failed: sourceId is not registered. sourceId='{sourceId ?? "<null>"}'.");
                return EditorLocalizationSetLanguageResult.NOT_REGISTERED;
            }

            var normalizedLanguageCode = NormalizeLanguageCode(languageCode);
            if (string.IsNullOrWhiteSpace(normalizedLanguageCode))
            {
                normalizedLanguageCode = EditorLocalizationConstants.DefaultLanguageCode;
            }

            var targetLoadResult = _store.LoadTable(source, normalizedLanguageCode);
            if (targetLoadResult.Status == EditorLocalizationTableLoadStatus.Success)
            {
                _currentLanguageCode = normalizedLanguageCode;
                PersistCurrentLanguage();
                RaiseLanguageChanged(_currentLanguageCode);
                return EditorLocalizationSetLanguageResult.SUCCESS;
            }

            if (targetLoadResult.Status == EditorLocalizationTableLoadStatus.NotFound)
            {
                ShowLanguageNotFoundDialog(source.SourceId, normalizedLanguageCode);

                var fallbackCode = EditorLocalizationConstants.DefaultLanguageCode;
                var fallbackLoadResult = _store.LoadTable(source, fallbackCode);
                if (fallbackLoadResult.Status != EditorLocalizationTableLoadStatus.Success)
                {
                    Debug.LogError($"{EditorLocalizationConstants.LogPrefix} SetLanguage failed: {BuildLoadErrorMessage(source.SourceId, fallbackCode, fallbackLoadResult)}");
                    return EditorLocalizationSetLanguageResult.FAILED;
                }

                _currentLanguageCode = fallbackCode;
                PersistCurrentLanguage();
                RaiseLanguageChanged(_currentLanguageCode);
                return EditorLocalizationSetLanguageResult.NOT_FOUND;
            }

            Debug.LogError($"{EditorLocalizationConstants.LogPrefix} SetLanguage failed: {BuildLoadErrorMessage(source.SourceId, normalizedLanguageCode, targetLoadResult)}");
            return EditorLocalizationSetLanguageResult.FAILED;
        }

        public IReadOnlyList<string> GetAvailableLanguages(string sourceId)
        {
            EnsureInitialized();

            if (!TryGetSource(sourceId, out var source))
            {
                return Array.Empty<string>();
            }

            return _store.GetAvailableLanguages(source);
        }

        public string Get(string sourceId, string key, string fallback = null)
        {
            EnsureInitialized();
            EnsureRegistered();

            if (key == null)
            {
                return null;
            }

            if (!TryGetSource(sourceId, out var source))
            {
                return key;
            }

            if (!_store.TryResolveFolderPath(source, out _, out _))
            {
                return key;
            }

            foreach (var languageCode in BuildFallbackLanguageCodes(source))
            {
                var result = _store.LoadTable(source, languageCode);
                if (result.Status == EditorLocalizationTableLoadStatus.Success &&
                    result.Table.TryGetValue(key, out var localizedText))
                {
                    return localizedText;
                }

                if (result.Status == EditorLocalizationTableLoadStatus.Failed)
                {
                    Debug.LogError($"{EditorLocalizationConstants.LogPrefix} Get failed while loading table: {BuildLoadErrorMessage(source.SourceId, languageCode, result)}");
                }
            }

            if (fallback != null)
            {
                return fallback;
            }

            if (EditorPrefs.GetBool(EditorLocalizationConstants.LogMissingKeysEditorPrefsKey, false))
            {
                Debug.LogWarning($"{EditorLocalizationConstants.LogPrefix} Missing localization key. sourceId='{sourceId}', key='{key}', language='{_currentLanguageCode}'.");
            }

            return key;
        }

        public bool Reload(string sourceId)
        {
            EnsureInitialized();
            EnsureRegistered();

            if (!TryGetSource(sourceId, out var source))
            {
                return false;
            }

            _store.InvalidateSource(source.SourceId);
            if (!_store.TryResolveFolderPath(source, out _, out var resolveError))
            {
                Debug.LogError($"{EditorLocalizationConstants.LogPrefix} Reload failed: {resolveError}");
                return false;
            }

            var loadResult = _store.LoadTable(source, _currentLanguageCode);
            if (loadResult.Status == EditorLocalizationTableLoadStatus.Failed)
            {
                Debug.LogError($"{EditorLocalizationConstants.LogPrefix} Reload failed: {BuildLoadErrorMessage(source.SourceId, _currentLanguageCode, loadResult)}");
                return false;
            }

            return true;
        }

        public void ReloadAll()
        {
            EnsureInitialized();
            EnsureRegistered();

            _store.InvalidateAll();
            foreach (var source in _sources.Values)
            {
                if (!_store.TryResolveFolderPath(source, out _, out var resolveError))
                {
                    Debug.LogError($"{EditorLocalizationConstants.LogPrefix} ReloadAll resolve failed: {resolveError}");
                    continue;
                }

                var loadResult = _store.LoadTable(source, _currentLanguageCode);
                if (loadResult.Status == EditorLocalizationTableLoadStatus.Failed)
                {
                    Debug.LogError($"{EditorLocalizationConstants.LogPrefix} ReloadAll load failed: {BuildLoadErrorMessage(source.SourceId, _currentLanguageCode, loadResult)}");
                }
            }
        }

        public EditorLocalizationValidationResult ValidateLanguageDiff(string sourceId, string targetLanguageCode)
        {
            EnsureInitialized();
            EnsureRegistered();

            if (!TryGetSource(sourceId, out var source))
            {
                return new EditorLocalizationValidationResult
                {
                    SourceId = sourceId ?? string.Empty,
                    BaseLanguageCode = string.Empty,
                    TargetLanguageCode = targetLanguageCode ?? string.Empty,
                    MissingKeys = Array.Empty<string>(),
                    ExtraKeys = Array.Empty<string>(),
                    ParseErrors = new[] { $"Source is not registered: {sourceId ?? "<null>"}" },
                    HasError = true
                };
            }

            var normalizedTargetLanguageCode = NormalizeLanguageCode(targetLanguageCode);
            if (string.IsNullOrWhiteSpace(normalizedTargetLanguageCode))
            {
                normalizedTargetLanguageCode = source.DefaultLanguageCode;
            }

            return _validationService.Validate(source, normalizedTargetLanguageCode);
        }

        private void EnsureInitialized()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;

            string candidateLanguageCode;
            if (EditorPrefs.HasKey(EditorLocalizationConstants.CurrentLanguageEditorPrefsKey))
            {
                candidateLanguageCode = NormalizeLanguageCode(
                    EditorPrefs.GetString(
                        EditorLocalizationConstants.CurrentLanguageEditorPrefsKey,
                        EditorLocalizationConstants.DefaultLanguageCode));
            }
            else
            {
                candidateLanguageCode = NormalizeLanguageCode(CultureInfo.CurrentUICulture?.Name);
            }

            if (!EditorLocalizationStore.TryNormalizeStrictLanguageCode(candidateLanguageCode, out var strictLanguageCode))
            {
                strictLanguageCode = EditorLocalizationConstants.DefaultLanguageCode;
            }

            _currentLanguageCode = strictLanguageCode;
            PersistCurrentLanguage();
        }

        private void EnsureRegistered()
        {
            if (_sources.Count > 0)
            {
                return;
            }

            if (_missingSourceWarningLogged)
            {
                return;
            }

            _missingSourceWarningLogged = true;
            Debug.LogWarning($"{EditorLocalizationConstants.LogPrefix} No localization sources are registered.");
        }

        private bool TryGetSource(string sourceId, out EditorLocalizationSourceDefinition source)
        {
            source = null;
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return false;
            }

            return _sources.TryGetValue(sourceId, out source);
        }

        private void PersistCurrentLanguage()
        {
            EditorPrefs.SetString(
                EditorLocalizationConstants.CurrentLanguageEditorPrefsKey,
                _currentLanguageCode ?? EditorLocalizationConstants.DefaultLanguageCode);
        }

        private static EditorLocalizationSourceDefinition NormalizeSourceDefinition(EditorLocalizationSourceDefinition source)
        {
            var defaultLanguageCode = NormalizeLanguageCode(source.DefaultLanguageCode);
            if (!EditorLocalizationStore.TryNormalizeStrictLanguageCode(defaultLanguageCode, out var normalizedDefaultLanguage))
            {
                normalizedDefaultLanguage = EditorLocalizationConstants.DefaultLanguageCode;
            }

            var baseLanguageCode = NormalizeLanguageCode(source.BaseLanguageCode);
            if (!EditorLocalizationStore.TryNormalizeStrictLanguageCode(baseLanguageCode, out var normalizedBaseLanguage))
            {
                normalizedBaseLanguage = EditorLocalizationConstants.DefaultLanguageCode;
            }

            return new EditorLocalizationSourceDefinition
            {
                SourceId = source.SourceId.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(source.DisplayName) ? source.SourceId.Trim() : source.DisplayName.Trim(),
                LocalizationFolderGuid = source.LocalizationFolderGuid?.Trim() ?? string.Empty,
                DefaultLanguageCode = normalizedDefaultLanguage,
                BaseLanguageCode = normalizedBaseLanguage
            };
        }

        private static string NormalizeLanguageCode(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return string.Empty;
            }

            var trimmed = languageCode.Trim().Replace('_', '-');
            var segments = trimmed.Split('-');
            if (segments.Length != 2 ||
                segments[0].Length != 2 ||
                segments[1].Length != 2)
            {
                return trimmed;
            }

            return $"{segments[0].ToLowerInvariant()}-{segments[1].ToUpperInvariant()}";
        }

        private static string BuildLoadErrorMessage(
            string sourceId,
            string languageCode,
            EditorLocalizationTableLoadResult loadResult)
        {
            if (loadResult.Status == EditorLocalizationTableLoadStatus.NotFound)
            {
                return $"Language file not found. sourceId='{sourceId}', languageCode='{languageCode}'.";
            }

            var message = string.IsNullOrWhiteSpace(loadResult.ErrorMessage)
                ? $"Failed to load language table. sourceId='{sourceId}', languageCode='{languageCode}'."
                : loadResult.ErrorMessage;

            if (loadResult.ParseErrors.Count == 0)
            {
                return message;
            }

            return $"{message} ParseErrors: {string.Join(" | ", loadResult.ParseErrors)}";
        }

        private static void ShowInvalidSourceCallDialog(string sourceId)
        {
            var displaySourceId = string.IsNullOrWhiteSpace(sourceId) ? "<null/empty>" : sourceId;
            EditorUtility.DisplayDialog(
                "Localization Error",
                $"SetLanguage was called with an invalid sourceId.\nsourceId: {displaySourceId}",
                "OK");
        }

        private static void ShowLanguageNotFoundDialog(string sourceId, string languageCode)
        {
            EditorUtility.DisplayDialog(
                "Localization Not Found",
                $"The language file was not found for the requested source.\nsourceId: {sourceId}\nlanguageCode: {languageCode}\n\nFallback to en-US will be applied.",
                "OK");
        }

        private IReadOnlyList<string> BuildFallbackLanguageCodes(EditorLocalizationSourceDefinition source)
        {
            var codes = new List<string>(3);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddFallbackCode(codes, seen, _currentLanguageCode);
            AddFallbackCode(codes, seen, source.DefaultLanguageCode);
            AddFallbackCode(codes, seen, EditorLocalizationConstants.DefaultLanguageCode);
            return codes;
        }

        private static void AddFallbackCode(ICollection<string> result, ISet<string> seen, string languageCode)
        {
            var normalizedCode = NormalizeLanguageCode(languageCode);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return;
            }

            if (!seen.Add(normalizedCode))
            {
                return;
            }

            result.Add(normalizedCode);
        }

        private void RaiseLanguageChanged(string languageCode)
        {
            if (LanguageChanged == null)
            {
                return;
            }

            foreach (var callback in LanguageChanged.GetInvocationList())
            {
                if (callback is not Action<string> action)
                {
                    continue;
                }

                try
                {
                    action(languageCode);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{EditorLocalizationConstants.LogPrefix} LanguageChanged callback failed: {ex.Message}");
                }
            }
        }
    }
}
