using UnityEditor;
using UnityEngine;

namespace Naninovel
{
    public class Live2DSettings : ConfigurationSettings<Live2DConfiguration>
    {
        protected override string HelpUri => "guide/characters.html#live2d-characters";

        private const string projectUrl = @"https://github.com/Elringus/NaninovelLive2D";

        protected override void DrawConfigurationEditor ()
        {
            DrawDefaultEditor();

            EditorGUILayout.Space();

            if (GUILayout.Button("GitHub Project"))
                Application.OpenURL(projectUrl);
        }
    }
}
