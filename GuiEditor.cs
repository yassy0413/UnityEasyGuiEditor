#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEasyGuiEditor
{
    [DisallowMultipleComponent]
    public class GuiEditor : MonoBehaviour, IDisposable
    {
        public static GuiEditor? Instance { get; private set; }

        private const int HeaderIconHeight = 24;

        [SerializeField]
        private int m_Resolution = 360;

        [SerializeField, Range(1, 8)]
        private int m_GroupSelectionXCount = 2;

        [SerializeField, Range(0, 1)]
        private float m_MatchHeight;

        [SerializeField]
        private bool m_DontDestroyOnLoad;

        [SerializeField]
        private bool m_ApplySafeArea = true;

        [SerializeField]
        private bool m_EnableBreadcrumb = true;

        [SerializeField]
        private bool m_DragWindow = true;

        [SerializeField]
        private Rect m_WindowRect = Rect.zero;

        [SerializeField]
        private string m_RootName = "[]";

        [SerializeField]
        private Color m_Color = Color.white;

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
        private Entry[] m_FilteredEntries = Array.Empty<Entry>();
        private GUI.WindowFunction m_WindowFunction = _ => { };

        private GUIContentWithSize? m_FilterGuiContent;
        private GUIContentWithSize? m_GreaterGuiContent;
        private GUIContentWithSize? m_BackGuiContent;
        private GUIContentWithSize? m_HomeGuiContent;
        private GUIContentWithSize? m_CloseGuiContent;
        private GUIContentWithSize? m_OpenGuiContent;

        public Entry? CurrentEntry { get; private set; }

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

        public void Dispose()
        {
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
            m_FilterGuiContent ??= new GUIContentWithSize("Filter:", GUI.skin.button);
            m_GreaterGuiContent ??= new GUIContentWithSize(">");
            m_BackGuiContent ??= new GUIContentWithSize("Back", GUI.skin.button);
            m_HomeGuiContent ??= new GUIContentWithSize("Home", GUI.skin.button);
            m_CloseGuiContent ??= new GUIContentWithSize("Close", GUI.skin.button);
            m_OpenGuiContent ??= new GUIContentWithSize("Open", GUI.skin.button);
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseDrag)
            {
                m_ScrollPosition -= Event.current.delta;
                m_BreadcrumbScrollPosition.x -= Event.current.delta.x;
            }

            EnsureGuiContents();

            var rect = m_ApplySafeArea
                ? Screen.safeArea
                : new Rect(0, 0, Screen.width, Screen.height);

            var scaleW = rect.width / m_Resolution;
            var scaleH = rect.height / m_Resolution;
            var scale = Mathf.Lerp(scaleW, scaleH, m_MatchHeight);
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

            var storedColor = GUI.color;
            GUI.color = m_Color;

            m_WindowRect = GUI.Window(
                GetInstanceID(), m_WindowRect, m_WindowFunction, string.Empty, GUI.skin.box);

            GUI.color = storedColor;
        }
    }
}