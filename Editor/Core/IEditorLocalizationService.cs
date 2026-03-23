using System;
using System.Collections.Generic;

namespace com.amari_noa.unity_editor_localization_core.editor
{
    public interface IEditorLocalizationService
    {
        string CurrentLanguageCode { get; }
        IReadOnlyList<string> RegisteredSourceIds { get; }

        bool RegisterSource(EditorLocalizationSourceDefinition source);
        bool UnregisterSource(string sourceId);

        EditorLocalizationSetLanguageResult SetLanguage(string sourceId, string languageCode);
        IReadOnlyList<string> GetAvailableLanguages(string sourceId);

        string Get(string sourceId, string key, string fallback = null);

        bool Reload(string sourceId);
        void ReloadAll();
        EditorLocalizationValidationResult ValidateLanguageDiff(string sourceId, string targetLanguageCode);

        event Action<string> LanguageChanged;
    }
}
