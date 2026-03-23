using System.Collections.Generic;

namespace com.amari_noa.unity_editor_localization_core.editor
{
    public sealed class EditorLocalizationValidationResult
    {
        public string SourceId;
        public string BaseLanguageCode;
        public string TargetLanguageCode;
        public IReadOnlyList<string> MissingKeys;
        public IReadOnlyList<string> ExtraKeys;
        public IReadOnlyList<string> ParseErrors;
        public bool HasError;
    }
}
