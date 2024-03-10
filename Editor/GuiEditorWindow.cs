using UnityEditor;

namespace UnityEasyGuiEditor.Editor
{
    public sealed class GuiEditorWindow : EditorWindow
    {
        private bool m_FullAccuracy;

        [MenuItem("Window/GUI Editor")]
        public static void ToggleShow()
        {
            var window = GetWindow<GuiEditorWindow>(false, "GUI Editor");
            window.Show();
        }

        private void Update()
        {
            var instance = GuiEditor.Instance;

            if (instance == null)
            {
                return;
            }

            if (m_FullAccuracy)
            {
                Repaint();
            }
            
            if (instance.enabled)
            {
                return;
            }

            instance.Update();
        }

        private void OnGUI()
        {
            var instance = GuiEditor.Instance;

            if (instance == null)
            {
                return;
            }

            m_FullAccuracy = EditorGUILayout
                .ToggleLeft("FullAccuracy", m_FullAccuracy);

            instance.EnsureGuiContents();
            instance.CurrentEntry.OnGUI();
        }
    }
}
