using UnityEditor;
using UnityEngine;

namespace Naninovel
{
    [CustomEditor(typeof(Live2DConfiguration))]
    public class Live2DConfigurationEditor : Editor
    {
        private const string projectUrl = @"https://github.com/Elringus/NaninovelLive2D";

        public override void OnInspectorGUI ()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "m_Script");

            EditorGUILayout.Space();

            if (GUILayout.Button("GitHub Project"))
                Application.OpenURL(projectUrl);

            serializedObject.ApplyModifiedProperties();
        }

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider ()
        {
            return AssetSettingsProvider.CreateProviderFromResourcePath("Project/Naninovel/Live2D", Live2DConfiguration.ResourcePath);
        }
    }
}
