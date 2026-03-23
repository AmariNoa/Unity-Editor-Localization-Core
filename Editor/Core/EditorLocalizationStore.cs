using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace com.amari_noa.unity_editor_localization_core.editor
{
    internal enum EditorLocalizationTableLoadStatus
    {
        Success,
        NotFound,
        Failed
    }

    internal sealed class EditorLocalizationTableLoadResult
    {
        public EditorLocalizationTableLoadStatus Status { get; }
        public IReadOnlyDictionary<string, string> Table { get; }
        public IReadOnlyList<string> ParseErrors { get; }
        public string ErrorMessage { get; }

        private EditorLocalizationTableLoadResult(
            EditorLocalizationTableLoadStatus status,
            IReadOnlyDictionary<string, string> table,
            IReadOnlyList<string> parseErrors,
            string errorMessage)
        {
            Status = status;
            Table = table;
            ParseErrors = parseErrors ?? Array.Empty<string>();
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public static EditorLocalizationTableLoadResult Success(IReadOnlyDictionary<string, string> table)
        {
            return new EditorLocalizationTableLoadResult(
                EditorLocalizationTableLoadStatus.Success,
                table ?? new Dictionary<string, string>(StringComparer.Ordinal),
                Array.Empty<string>(),
                string.Empty);
        }

        public static EditorLocalizationTableLoadResult NotFound()
        {
            return new EditorLocalizationTableLoadResult(
                EditorLocalizationTableLoadStatus.NotFound,
                null,
                Array.Empty<string>(),
                string.Empty);
        }

        public static EditorLocalizationTableLoadResult Failed(string message, IReadOnlyList<string> parseErrors = null)
        {
            return new EditorLocalizationTableLoadResult(
                EditorLocalizationTableLoadStatus.Failed,
                null,
                parseErrors ?? Array.Empty<string>(),
                message ?? "Localization table load failed.");
        }
    }

    internal sealed class EditorLocalizationStore
    {
        private static readonly Regex StrictLanguageCodeRegex = new("^[a-zA-Z]{2}-[a-zA-Z]{2}$", RegexOptions.Compiled);

        private readonly EditorLocalizationJsonFlattener _flattener;
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _tableCache;
        private readonly Dictionary<string, string> _resolvedFolderPathCache;

        public EditorLocalizationStore(EditorLocalizationJsonFlattener flattener)
        {
            _flattener = flattener ?? throw new ArgumentNullException(nameof(flattener));
            _tableCache = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            _resolvedFolderPathCache = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public void InvalidateSource(string sourceId, bool clearResolvedFolderPath = true)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return;
            }

            var prefix = $"{sourceId}::";
            var removeKeys = new List<string>();
            foreach (var pair in _tableCache)
            {
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    removeKeys.Add(pair.Key);
                }
            }

            foreach (var key in removeKeys)
            {
                _tableCache.Remove(key);
            }

            if (clearResolvedFolderPath)
            {
                _resolvedFolderPathCache.Remove(sourceId);
            }
        }

        public void InvalidateAll()
        {
            _tableCache.Clear();
            _resolvedFolderPathCache.Clear();
        }

        public bool TryResolveFolderPath(
            EditorLocalizationSourceDefinition source,
            out string folderAssetPath,
            out string errorMessage)
        {
            folderAssetPath = null;
            errorMessage = string.Empty;

            if (source == null)
            {
                errorMessage = "Localization source definition is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(source.SourceId))
            {
                errorMessage = "Localization sourceId is empty.";
                return false;
            }

            if (_resolvedFolderPathCache.TryGetValue(source.SourceId, out var cachedPath) &&
                !string.IsNullOrWhiteSpace(cachedPath))
            {
                var cachedAbsolutePath = ToAbsolutePath(cachedPath);
                if (Directory.Exists(cachedAbsolutePath))
                {
                    folderAssetPath = cachedPath;
                    return true;
                }

                _resolvedFolderPathCache.Remove(source.SourceId);
            }

            if (string.IsNullOrWhiteSpace(source.LocalizationFolderGuid))
            {
                errorMessage = $"LocalizationFolderGuid is empty for sourceId '{source.SourceId}'.";
                return false;
            }

            var resolvedPath = AssetDatabase.GUIDToAssetPath(source.LocalizationFolderGuid);
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                errorMessage = $"Failed to resolve LocalizationFolderGuid '{source.LocalizationFolderGuid}' for sourceId '{source.SourceId}'.";
                return false;
            }

            var absolutePath = ToAbsolutePath(resolvedPath);
            if (!Directory.Exists(absolutePath))
            {
                errorMessage = $"Localization folder path does not exist: {resolvedPath}";
                return false;
            }

            _resolvedFolderPathCache[source.SourceId] = resolvedPath;
            folderAssetPath = resolvedPath;
            return true;
        }

        public IReadOnlyList<string> GetAvailableLanguages(EditorLocalizationSourceDefinition source)
        {
            if (!TryResolveFolderPath(source, out var folderAssetPath, out _))
            {
                return Array.Empty<string>();
            }

            var absolutePath = ToAbsolutePath(folderAssetPath);
            if (!Directory.Exists(absolutePath))
            {
                return Array.Empty<string>();
            }

            var languageSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var filePath in Directory.EnumerateFiles(absolutePath, "*.json", SearchOption.TopDirectoryOnly))
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                if (!TryNormalizeStrictLanguageCode(fileNameWithoutExtension, out var normalized))
                {
                    continue;
                }

                languageSet.Add(normalized);
            }

            var result = new List<string>(languageSet);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public EditorLocalizationTableLoadResult LoadTable(EditorLocalizationSourceDefinition source, string languageCode)
        {
            if (source == null)
            {
                return EditorLocalizationTableLoadResult.Failed("Localization source definition is null.");
            }

            if (string.IsNullOrWhiteSpace(source.SourceId))
            {
                return EditorLocalizationTableLoadResult.Failed("Localization sourceId is empty.");
            }

            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return EditorLocalizationTableLoadResult.NotFound();
            }

            var cacheKey = BuildCacheKey(source.SourceId, languageCode);
            if (_tableCache.TryGetValue(cacheKey, out var cachedTable))
            {
                return EditorLocalizationTableLoadResult.Success(cachedTable);
            }

            if (!TryResolveFolderPath(source, out var folderAssetPath, out var resolveError))
            {
                return EditorLocalizationTableLoadResult.Failed(resolveError);
            }

            var jsonAssetPath = $"{folderAssetPath.TrimEnd('/')}/{languageCode}.json";
            var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonAssetPath);
            if (jsonAsset == null)
            {
                return EditorLocalizationTableLoadResult.NotFound();
            }

            if (!_flattener.TryParseAndFlatten(jsonAsset.text, out var table, out var parseErrors))
            {
                var message = $"Failed to parse localization JSON: {jsonAssetPath}";
                return EditorLocalizationTableLoadResult.Failed(message, parseErrors);
            }

            _tableCache[cacheKey] = table;
            return EditorLocalizationTableLoadResult.Success(table);
        }

        private static string BuildCacheKey(string sourceId, string languageCode)
        {
            return $"{sourceId}::{languageCode}";
        }

        private static string ToAbsolutePath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            var normalized = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(normalized);
        }

        internal static bool TryNormalizeStrictLanguageCode(string languageCode, out string normalizedLanguageCode)
        {
            normalizedLanguageCode = string.Empty;
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return false;
            }

            var trimmed = languageCode.Trim();
            if (!StrictLanguageCodeRegex.IsMatch(trimmed))
            {
                return false;
            }

            var segments = trimmed.Split('-');
            if (segments.Length != 2)
            {
                return false;
            }

            normalizedLanguageCode = $"{segments[0].ToLowerInvariant()}-{segments[1].ToUpperInvariant()}";
            return true;
        }
    }
}
