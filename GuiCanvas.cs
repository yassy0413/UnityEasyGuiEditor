using System;
using UnityEngine;

namespace UnityEasyGuiEditor
{
    [DisallowMultipleComponent]
    public class GuiCanvas : MonoBehaviour
    {
        public static Action<Rect> OnGui;

        [SerializeField]
        private int m_Resolution = 360;

        [SerializeField, Range(0, 1)]
        private float m_MatchHeight;

        [SerializeField]
        private bool m_ApplySafeArea = true;

        private void OnGUI()
        {
            if (OnGui == null)
            {
                return;
            }

            var rect = m_ApplySafeArea
                ? Screen.safeArea
                : new Rect(0, 0, Screen.width, Screen.height);

            var scaleW = rect.width / m_Resolution;
            var scaleH = rect.height / m_Resolution;
            var scale = Mathf.Lerp(scaleW, scaleH, m_MatchHeight);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), Vector2.zero);

            var invScale = 1 / scale;
            var aspect = rect.height / rect.width;
            var contentRect = new Rect(rect.x * invScale, rect.y * invScale, m_Resolution, m_Resolution * aspect);

            using var areaScope = new GUILayout.AreaScope(contentRect);
            OnGui.Invoke(contentRect);
        }
    }
}
