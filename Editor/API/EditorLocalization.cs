using UnityEditor;

namespace com.amari_noa.unity_editor_localization_core.editor
{
    [InitializeOnLoad]
    public static class EditorLocalization
    {
        public static IEditorLocalizationService Service { get; }

        static EditorLocalization()
        {
            Service = new EditorLocalizationService();
        }
    }
}
