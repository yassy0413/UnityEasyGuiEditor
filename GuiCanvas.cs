using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEasyGuiEditor
{
    [DisallowMultipleComponent]
    public class GuiCanvas : MonoBehaviour, IDisposable
    {
        public static GuiCanvas Instance { get; private set; }

        [SerializeField]
        private int m_Resolution = 360;

        [SerializeField, Range(0, 1)]
        private float m_MatchHeight;

        [SerializeField]
        private bool m_ApplySafeArea = true;

        public List<Action<Rect>> ActionList { get; } = new();

        public static void Add(Action<Rect> action)
        {
            Instance.ActionList.Add(action);
        }

        public static void Remove(Action<Rect> action)
        {
            Instance.ActionList.Remove(action);
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            Dispose();
        }

        public void Dispose()
        {
            ActionList.Clear();
            Instance = null;
        }

        private void OnGUI()
        {
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

            foreach (var act in ActionList)
            {
                act.Invoke(contentRect);
            }
        }
    }
}
