#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEasyGuiEditor
{
    [DisallowMultipleComponent]
    public class GuiEditor : MonoBehaviour, IDisposable
    {
        public static GuiEditor? Instance { get; private set; }

        private const int HeaderIconHeight = 24;

        private static readonly int WindowId = typeof(GuiEditor).FullName!.GetHashCode();

        [SerializeField]
        private int m_Resolution = 360;

        [SerializeField, Range(1, 8)]
        private int m_GroupSelectionXCount = 2;

        [SerializeField, Range(0, 1)]
        private float m_MatchHeight;

        [SerializeField]
        private bool m_DontDestroyOnLoad;

        [SerializeField]
        private bool m_VisibleOnAwake;

        [SerializeField]
        private bool m_ApplySafeArea = true;

        [SerializeField]
        private bool m_EnableBreadcrumb = true;

        [SerializeField]
        private bool m_DragWindow = true;

        [SerializeField]
        private Rect m_WindowRect = Rect.zero;

        [SerializeField]
        private Color m_WindowColor = new(0f, 0f, 0f, 0.8f);

        [SerializeField]
        private string m_RootName = "[]";

        [SerializeField]
        private Texture2D? m_BackButtonTexture;

        [SerializeField]
        private Texture2D? m_HomeButtonTexture;

        [SerializeField]
        private Texture2D? m_CloseButtonTexture;

        [SerializeField]
        private Texture2D? m_FilterButtonTexture;

        private Entry? m_EntryRoot;
        private Entry? m_EntryNext;
        private Vector2 m_ScrollPosition;
        private Vector2 m_BreadcrumbScrollPosition;
        private string m_Filter = string.Empty;
        private bool m_FilterUpdated;
        private bool m_BlockMouseEvent;
        private Canvas? m_EventBlockerCanvas;
        private RectTransform? m_EventBlockerRectTransform;
        private Entry[] m_FilteredEntries = Array.Empty<Entry>();
        private GUI.WindowFunction m_WindowFunction = _ => { };

        private GUIStyle? m_WindowStyle;
        private GUIContentWithSize? m_FilterGuiContent;
        private GUIContentWithSize? m_GreaterGuiContent;
        private GUIContentWithSize? m_BackGuiContent;
        private GUIContentWithSize? m_HomeGuiContent;
        private GUIContentWithSize? m_CloseGuiContent;
        private GUIContentWithSize? m_OpenGuiContent;

        public Entry? CurrentEntry { get; private set; }
        public bool IsVisible { get; set; }

        public void ToggleVisible()
        {
            IsVisible = !IsVisible;
        }

        public sealed class Entry
        {
            public string Name { get; set; } = string.Empty;
            public Entry? Parent { get; set; }
            public List<Entry> Entries { get; set; } = new();
            public Action OnGUI { get; set; } = () => { };

            private GUIContentWithSize? m_NameLabelGuiContent;

            public GUIContentWithSize NameLabelGUIContent =>
                m_NameLabelGuiContent ??= new GUIContentWithSize(Name, GUI.skin.label);

            private GUIContentWithSize? m_NameButtonGuiContent;

            public GUIContentWithSize NameButtonGUIContent =>
                m_NameButtonGuiContent ??= new GUIContentWithSize(Name, GUI.skin.button);

            public void Add(string name, Action<Entry> onGui)
            {
                if (Instance == null)
                {
                    return;
                }

                Entries.Add(Instance.CreateEntry(name, this, onGui));
            }

            public List<Entry> GetBreadcrumb()
            {
                var result = new List<Entry>();
                for (var entry = this; entry != null; entry = entry.Parent)
                {
                    result.Add(entry);
                }

                result.Reverse();
                return result;
            }

            public void BuildFilteredList(List<Entry> entries, string filter)
            {
                if (Name.ToLowerInvariant().Contains(filter))
                {
                    entries.Add(this);
                }

                foreach (var entry in Entries)
                {
                    entry.BuildFilteredList(entries, filter);
                }
            }
        }

        public sealed class GUIContentWithSize
        {
            public GUIContent GUIContent { get; }
            public GUILayoutOption Width { get; }

            public GUIContentWithSize(string value, GUIStyle? style = null)
            {
                style ??= GUI.skin.label;
                GUIContent = new GUIContent(value);
                Width = GUILayout.Width(style.CalcSize(GUIContent).x + 2);
            }
        }

        private void Awake()
        {
            Instance = this;

            m_EntryRoot = CreateRootEntry(m_RootName, null);
            CurrentEntry = m_EntryRoot;
            IsVisible = m_VisibleOnAwake;

            m_WindowFunction = _ =>
            {
                CurrentEntry.OnGUI();

                if (m_DragWindow)
                {
                    GUI.DragWindow();
                }
            };

            if (m_DontDestroyOnLoad)
            {
                DontDestroyOnLoad(this);
            }
        }

        private void OnDestroy()
        {
            Dispose();
        }

        private void OnDisable()
        {
            SetEventBlockerActive(false);
        }

        public void Dispose()
        {
            if (m_EventBlockerCanvas != null)
            {
                Destroy(m_EventBlockerCanvas.gameObject);
                m_EventBlockerCanvas = null;
                m_EventBlockerRectTransform = null;
            }

            m_EntryRoot = null;
            CurrentEntry = null;
            Instance = null;
        }

        public Entry? GetEntry(string path)
        {
            var entry = m_EntryRoot;

            if (entry == null)
            {
                return null;
            }

            foreach (var entryName in path.Split('/'))
            {
                var v = entry.Entries.FirstOrDefault(x => x.Name == entryName);
                if (v == null)
                {
                    v = CreateDirectoryEntry(entryName, entry);
                    entry.Entries.Add(v);
                }

                entry = v;
            }

            return entry;
        }

        public static void AddEntry(string path, Action<Entry> onGui)
        {
            if (Instance == null)
            {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            var entry = string.IsNullOrEmpty(directory)
                ? Instance.m_EntryRoot
                : Instance.GetEntry(directory);
            entry?.Add(Path.GetFileName(path), onGui);
        }

        private Entry CreateEntry(string entryName, Entry parent, Action<Entry> onGui)
        {
            var entry = new Entry
            {
                Name = entryName,
                Parent = parent
            };
            entry.OnGUI = () =>
            {
                DrawToolBar();

                if (m_EnableBreadcrumb)
                {
                    DrawBreadcrumb();
                }

                using var scrollViewScope = new GUILayout.ScrollViewScope(m_ScrollPosition);
                m_ScrollPosition = scrollViewScope.scrollPosition;
                onGui(entry);
            };
            return entry;
        }

        private Entry CreateDirectoryEntry(string entryName, Entry parent)
        {
            return CreateEntry(entryName, parent, DrawDirectory);
        }

        private Entry CreateRootEntry(string entryName, Entry? parent)
        {
            var entry = new Entry
            {
                Name = entryName,
                Parent = parent
            };
            entry.OnGUI = () =>
            {
                DrawSearch();

                using var scrollViewScope = new GUILayout.ScrollViewScope(m_ScrollPosition);
                m_ScrollPosition = scrollViewScope.scrollPosition;

                if (m_FilteredEntries.Length < 1)
                {
                    DrawDirectory(entry);
                }
                else
                {
                    DrawFilteredEntries();
                }
            };
            return entry;
        }

        private static bool HeaderButton(GUIContentWithSize? content, Texture2D? texture2D)
        {
            if (content == null)
            {
                return false;
            }

            return texture2D == null
                ? GUILayout.Button(content.GUIContent, content.Width)
                : GUILayout.Button(texture2D,
                    GUILayout.Width(HeaderIconHeight), GUILayout.Height(HeaderIconHeight));
        }

        private void DrawDirectory(Entry entry)
        {
            var entryCount = entry.Entries.Count;

            GUILayout.BeginHorizontal();

            for (var index = 0; index < entryCount; ++index)
            {
                if (index % m_GroupSelectionXCount == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }

                var childEntry = entry.Entries[index];

                if (GUILayout.Button(childEntry.NameButtonGUIContent.GUIContent))
                {
                    m_EntryNext = childEntry;
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawFilteredEntries()
        {
            var entryCount = m_FilteredEntries.Length;

            GUILayout.BeginHorizontal();

            for (var index = 0; index < entryCount; ++index)
            {
                if (index % m_GroupSelectionXCount == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }

                var entry = m_FilteredEntries[index];

                if (GUILayout.Button(entry.NameButtonGUIContent.GUIContent))
                {
                    m_EntryNext = entry;
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawSearch()
        {
            using var _ = new GUILayout.HorizontalScope(GUI.skin.box);

            if (m_FilterButtonTexture == null)
            {
                if (m_FilterGuiContent != null)
                {
                    GUILayout.Label(m_FilterGuiContent.GUIContent, m_FilterGuiContent.Width);
                }
            }
            else
            {
                GUILayout.Label(m_FilterButtonTexture,
                    GUILayout.Width(HeaderIconHeight), GUILayout.Height(HeaderIconHeight));
            }

            var filter = GUILayout.TextArea(m_Filter);
            if (filter != m_Filter)
            {
                m_Filter = filter;
                m_FilterUpdated = true;
            }

            ToggleOpenButton();
        }

        private void DrawToolBar()
        {
            using var _ = new GUILayout.HorizontalScope(GUI.skin.box);

            if (CurrentEntry?.Parent != null)
            {
                if (HeaderButton(m_BackGuiContent, m_BackButtonTexture))
                {
                    m_EntryNext = CurrentEntry.Parent;
                }

                if (HeaderButton(m_HomeGuiContent, m_HomeButtonTexture))
                {
                    m_EntryNext = m_EntryRoot;
                }
            }

            if (CurrentEntry != null)
            {
                GUILayout.Label(
                    CurrentEntry.NameLabelGUIContent.GUIContent,
                    CurrentEntry.NameLabelGUIContent.Width);
            }

            GUILayout.FlexibleSpace();

            ToggleOpenButton();
        }

        private void ToggleOpenButton()
        {
            if (enabled)
            {
                if (HeaderButton(m_CloseGuiContent, m_CloseButtonTexture))
                {
                    enabled = false;
                }
            }
            else
            {
                if (m_OpenGuiContent != null)
                {
                    if (GUILayout.Button(m_OpenGuiContent.GUIContent, m_OpenGuiContent.Width))
                    {
                        enabled = true;
                    }
                }
            }
        }

        private void DrawBreadcrumb()
        {
            if (CurrentEntry?.Parent == null)
            {
                return;
            }

            using var scrollViewScope = new GUILayout.ScrollViewScope(m_BreadcrumbScrollPosition, GUILayout.Height(54));
            m_BreadcrumbScrollPosition = scrollViewScope.scrollPosition;

            using var _ = new GUILayout.HorizontalScope(GUI.skin.box);

            var breadcrumb = CurrentEntry.GetBreadcrumb();
            var breadcrumbCount = breadcrumb.Count;

            for (var index = 0; index < breadcrumbCount; ++index)
            {
                var entry = breadcrumb[index];

                if (index + 1 < breadcrumbCount)
                {
                    var nameGuiContent = entry.NameButtonGUIContent;
                    if (GUILayout.Button(nameGuiContent.GUIContent, nameGuiContent.Width))
                    {
                        m_EntryNext = entry;
                    }

                    if (m_GreaterGuiContent != null)
                    {
                        GUILayout.Label(m_GreaterGuiContent.GUIContent, m_GreaterGuiContent.Width);
                    }
                }
                else
                {
                    var nameGuiContent = entry.NameLabelGUIContent;
                    GUILayout.Label(nameGuiContent.GUIContent, nameGuiContent.Width);
                }
            }
        }

        public void Update()
        {
            if (m_EntryNext != null)
            {
                CurrentEntry = m_EntryNext;
                m_EntryNext = null;
                m_ScrollPosition = Vector2.zero;
                m_BreadcrumbScrollPosition = Vector2.zero;
            }

            if (m_FilterUpdated)
            {
                m_FilterUpdated = false;

                if (string.IsNullOrEmpty(m_Filter))
                {
                    m_FilteredEntries = Array.Empty<Entry>();
                }
                else
                {
                    var filteredEntries = new List<Entry>();
                    m_EntryRoot?.BuildFilteredList(filteredEntries, m_Filter.ToLowerInvariant());
                    m_FilteredEntries = filteredEntries.ToArray();
                }
            }
        }

        public void EnsureGuiContents()
        {
            if (m_WindowStyle == null)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, m_WindowColor);
                tex.Apply();

                m_WindowStyle = new GUIStyle(GUI.skin.box);
                m_WindowStyle.normal.background = tex;
            }

            m_FilterGuiContent ??= new GUIContentWithSize("Filter:", GUI.skin.button);
            m_GreaterGuiContent ??= new GUIContentWithSize(">");
            m_BackGuiContent ??= new GUIContentWithSize("Back", GUI.skin.button);
            m_HomeGuiContent ??= new GUIContentWithSize("Home", GUI.skin.button);
            m_CloseGuiContent ??= new GUIContentWithSize("Close", GUI.skin.button);
            m_OpenGuiContent ??= new GUIContentWithSize("Open", GUI.skin.button);
        }

        private void OnGUI()
        {
            if (!IsVisible)
            {
                SetEventBlockerActive(false);
                return;
            }

            EnsureGuiContents();

            var rect = GetGuiArea();
            var scale = GetGuiScale(rect);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), Vector2.zero);

            if (m_WindowRect == Rect.zero)
            {
                var invScale = 1 / scale;
                var aspect = rect.height / rect.width;
                m_WindowRect = new Rect(
                    rect.x * invScale,
                    rect.y * invScale,
                    m_Resolution,
                    m_Resolution * aspect);
            }

            var currentEvent = Event.current;
            var eventType = currentEvent.type;
            var mousePosition = currentEvent.mousePosition / scale;
            var isMouseEvent = IsMouseEvent(currentEvent);
            var isMouseEventInWindow = isMouseEvent && m_WindowRect.Contains(mousePosition);
            var shouldBlockMouseEvent = isMouseEvent && (isMouseEventInWindow || m_BlockMouseEvent);

            if (isMouseEventInWindow && eventType == EventType.MouseDown)
            {
                m_BlockMouseEvent = true;
            }

            if (shouldBlockMouseEvent && eventType == EventType.MouseDrag)
            {
                m_ScrollPosition -= currentEvent.delta / scale;
                m_BreadcrumbScrollPosition.x -= currentEvent.delta.x / scale;
            }

            m_WindowRect = GUI.Window(WindowId, m_WindowRect, m_WindowFunction, string.Empty, m_WindowStyle);

            UpdateEventBlocker(scale);

            if (shouldBlockMouseEvent)
            {
                currentEvent.Use();
            }

            if (eventType == EventType.MouseUp)
            {
                m_BlockMouseEvent = false;
            }
        }

        private static bool IsMouseEvent(Event e)
        {
            return e.type is EventType.MouseDown or EventType.MouseUp or EventType.MouseDrag or EventType.ScrollWheel;
        }

        private void UpdateEventBlocker(float scale)
        {
            EnsureEventBlocker();

            if (m_EventBlockerCanvas == null || m_EventBlockerRectTransform == null)
            {
                return;
            }

            m_EventBlockerCanvas.gameObject.SetActive(IsVisible);

            if (!IsVisible)
            {
                return;
            }

            var screenRect = new Rect(
                m_WindowRect.x * scale,
                m_WindowRect.y * scale,
                m_WindowRect.width * scale,
                m_WindowRect.height * scale);

            m_EventBlockerRectTransform.anchoredPosition =
                new Vector2(screenRect.xMin, Screen.height - screenRect.yMax);
            m_EventBlockerRectTransform.sizeDelta = screenRect.size;
        }

        private void SetEventBlockerActive(bool active)
        {
            if (m_EventBlockerCanvas != null)
            {
                m_EventBlockerCanvas.gameObject.SetActive(active);
            }
        }

        private void EnsureEventBlocker()
        {
            if (m_EventBlockerCanvas != null)
            {
                return;
            }

            var blocker = new GameObject(
                $"{nameof(GuiEditor)}EventBlocker",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            blocker.transform.SetParent(transform, false);

            m_EventBlockerCanvas = blocker.GetComponent<Canvas>();
            m_EventBlockerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_EventBlockerCanvas.overrideSorting = true;
            m_EventBlockerCanvas.sortingOrder = short.MaxValue;

            var canvasRectTransform = blocker.GetComponent<RectTransform>();
            canvasRectTransform.anchorMin = Vector2.zero;
            canvasRectTransform.anchorMax = Vector2.one;
            canvasRectTransform.offsetMin = Vector2.zero;
            canvasRectTransform.offsetMax = Vector2.zero;

            var imageObject = new GameObject(
                "Blocker",
                typeof(RectTransform),
                typeof(Image));
            imageObject.transform.SetParent(blocker.transform, false);

            m_EventBlockerRectTransform = imageObject.GetComponent<RectTransform>();
            m_EventBlockerRectTransform.anchorMin = Vector2.zero;
            m_EventBlockerRectTransform.anchorMax = Vector2.zero;
            m_EventBlockerRectTransform.pivot = Vector2.zero;

            var image = imageObject.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;

            blocker.SetActive(false);
        }

        private Rect GetGuiArea()
        {
            return m_ApplySafeArea
                ? Screen.safeArea
                : new Rect(0, 0, Screen.width, Screen.height);
        }

        private float GetGuiScale(Rect rect)
        {
            var scaleW = rect.width / m_Resolution;
            var scaleH = rect.height / m_Resolution;
            return Mathf.Lerp(scaleW, scaleH, m_MatchHeight);
        }
    }
}