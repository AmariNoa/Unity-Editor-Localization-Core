using System;
using System.Collections.Generic;

namespace com.amari_noa.unity_editor_localization_core.editor
{
    internal sealed class EditorLocalizationValidationService
    {
        private readonly EditorLocalizationStore _store;

        public EditorLocalizationValidationService(EditorLocalizationStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public EditorLocalizationValidationResult Validate(
            EditorLocalizationSourceDefinition source,
            string targetLanguageCode)
        {
            var parseErrors = new List<string>();
            var missingKeys = new List<string>();
            var extraKeys = new List<string>();

            if (source == null || string.IsNullOrWhiteSpace(source.SourceId))
            {
                parseErrors.Add("Source is not registered.");
                return BuildResult(source, targetLanguageCode, missingKeys, extraKeys, parseErrors, hasError: true);
            }

            var baseLanguageCode = string.IsNullOrWhiteSpace(source.BaseLanguageCode)
                ? EditorLocalizationConstants.DefaultLanguageCode
                : source.BaseLanguageCode;

            var targetCode = string.IsNullOrWhiteSpace(targetLanguageCode)
                ? source.DefaultLanguageCode
                : targetLanguageCode;

            var baseTableResult = _store.LoadTable(source, baseLanguageCode);
            var targetTableResult = _store.LoadTable(source, targetCode);

            if (baseTableResult.Status != EditorLocalizationTableLoadStatus.Success)
            {
                AppendLoadErrors(parseErrors, source.SourceId, baseLanguageCode, baseTableResult);
            }

            if (targetTableResult.Status != EditorLocalizationTableLoadStatus.Success)
            {
                AppendLoadErrors(parseErrors, source.SourceId, targetCode, targetTableResult);
            }

            if (parseErrors.Count > 0)
            {
                return BuildResult(
                    source,
                    targetCode,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    parseErrors,
                    hasError: true);
            }

            var baseKeySet = new HashSet<string>(baseTableResult.Table.Keys, StringComparer.Ordinal);
            var targetKeySet = new HashSet<string>(targetTableResult.Table.Keys, StringComparer.Ordinal);

            foreach (var key in baseKeySet)
            {
                if (!targetKeySet.Contains(key))
                {
                    missingKeys.Add(key);
                }
            }

            foreach (var key in targetKeySet)
            {
                if (!baseKeySet.Contains(key))
                {
                    extraKeys.Add(key);
                }
            }

            missingKeys.Sort(StringComparer.Ordinal);
            extraKeys.Sort(StringComparer.Ordinal);

            return BuildResult(source, targetCode, missingKeys, extraKeys, parseErrors, hasError: false);
        }

        private static void AppendLoadErrors(
            ICollection<string> destination,
            string sourceId,
            string languageCode,
            EditorLocalizationTableLoadResult loadResult)
        {
            if (loadResult.Status == EditorLocalizationTableLoadStatus.NotFound)
            {
                destination.Add($"Language file not found. sourceId='{sourceId}', languageCode='{languageCode}'.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(loadResult.ErrorMessage))
            {
                destination.Add(loadResult.ErrorMessage);
            }

            foreach (var parseError in loadResult.ParseErrors)
            {
                destination.Add(parseError);
            }
        }

        private static EditorLocalizationValidationResult BuildResult(
            EditorLocalizationSourceDefinition source,
            string targetLanguageCode,
            IReadOnlyList<string> missingKeys,
            IReadOnlyList<string> extraKeys,
            IReadOnlyList<string> parseErrors,
            bool hasError)
        {
            return new EditorLocalizationValidationResult
            {
                SourceId = source?.SourceId ?? string.Empty,
                BaseLanguageCode = source?.BaseLanguageCode ?? string.Empty,
                TargetLanguageCode = targetLanguageCode ?? string.Empty,
                MissingKeys = missingKeys ?? Array.Empty<string>(),
                ExtraKeys = extraKeys ?? Array.Empty<string>(),
                ParseErrors = parseErrors ?? Array.Empty<string>(),
                HasError = hasError
            };
        }
    }
}
