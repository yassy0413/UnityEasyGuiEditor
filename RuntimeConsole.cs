#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEasyGuiEditor
{
    /// <summary>
    /// Log viewer drawn inside a GuiEditor entry.
    /// Logs are received on any thread and kept even while the menu is closed (oldest are dropped over MaxLogCount).
    /// The newest log is listed first. Tap a row to show the full message and stack trace above the list.
    /// </summary>
    public sealed class RuntimeConsole : IDisposable
    {
        [Serializable]
        public sealed class Settings
        {
            public int FontSize = 10;
            public int MaxLogCount = 500;
            public Color InfoColor = Color.white;
            public Color WarningColor = Color.yellow;
            public Color ErrorColor = new(1f, 0.4f, 0.4f);
        }

        private sealed class LogEntry
        {
            public string Message = string.Empty;
            public string StackTrace = string.Empty;
            public LogType Type;
            public string TimeText = string.Empty;
            public string Header = string.Empty;
        }

        private const int LevelCount = 3;

        // A single IMGUI label can not hold too many characters
        private const int MaxDetailLength = 8000;

        private static readonly string[] LevelNames = { "Log", "Warning", "Error" };

        private readonly Settings m_Settings;
        private readonly object m_Lock = new();
        private readonly LogEntry?[] m_Entries;
        private int m_Head;
        private int m_Count;
        private int m_Version;

        // Copy for the main thread. Rebuilt only when m_Version changes
        private readonly List<LogEntry> m_Snapshot = new();
        private readonly int[] m_Counts = new int[LevelCount];
        private readonly bool[] m_Filters = { true, true, true };
        private readonly string[] m_FilterLabels = new string[LevelCount];
        private int m_SyncedVersion = -1;
        private string m_TextFilter = string.Empty;

        private LogEntry? m_Selected;
        private string m_DetailText = string.Empty;

        private GUIStyle? m_FilterStyle;
        private GUIStyle? m_TextFilterStyle;
        private GUIStyle? m_DetailStyle;
        private readonly GUIStyle?[] m_RowStyles = new GUIStyle?[LevelCount];

        public RuntimeConsole(Settings settings)
        {
            m_Settings = settings;
            m_Entries = new LogEntry?[Mathf.Max(1, settings.MaxLogCount)];
            RebuildFilterLabels();
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
        }

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
        }

        public void Clear()
        {
            lock (m_Lock)
            {
                Array.Clear(m_Entries, 0, m_Entries.Length);
                m_Head = 0;
                m_Count = 0;
                m_Version++;
            }

            m_Selected = null;
        }

        // Called from any thread. Only add to the locked buffer here
        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            try
            {
                var timeText = DateTime.Now.ToString("HH:mm:ss");
                var message = condition ?? string.Empty;
                var entry = new LogEntry
                {
                    Message = message,
                    StackTrace = stackTrace ?? string.Empty,
                    Type = type,
                    TimeText = timeText,
                    Header = timeText + " " + FirstLine(message),
                };

                lock (m_Lock)
                {
                    m_Entries[(m_Head + m_Count) % m_Entries.Length] = entry;
                    if (m_Count < m_Entries.Length)
                    {
                        m_Count++;
                    }
                    else
                    {
                        m_Head = (m_Head + 1) % m_Entries.Length;
                    }

                    m_Version++;
                }
            }
            catch
            {
                // Throwing here would be logged again, so swallow it
            }
        }

        private static string FirstLine(string message)
        {
            var index = message.IndexOf('\n');
            return index < 0 ? message : message.Substring(0, index).TrimEnd('\r');
        }

        private static int LevelOf(LogType type)
        {
            return type switch
            {
                LogType.Warning => 1,
                LogType.Error or LogType.Assert or LogType.Exception => 2,
                _ => 0,
            };
        }

        private void Sync()
        {
            lock (m_Lock)
            {
                if (m_Version == m_SyncedVersion)
                {
                    return;
                }

                m_SyncedVersion = m_Version;
                m_Snapshot.Clear();
                for (var i = 0; i < m_Count; i++)
                {
                    var entry = m_Entries[(m_Head + i) % m_Entries.Length];
                    if (entry != null)
                    {
                        m_Snapshot.Add(entry);
                    }
                }
            }

            Array.Clear(m_Counts, 0, m_Counts.Length);
            foreach (var entry in m_Snapshot)
            {
                m_Counts[LevelOf(entry.Type)]++;
            }

            RebuildFilterLabels();
        }

        private void RebuildFilterLabels()
        {
            for (var i = 0; i < LevelCount; i++)
            {
                // Show the state with a mark, not only with a color
                m_FilterLabels[i] = (m_Filters[i] ? "[x]" : "[ ]") + LevelNames[i] + " " + m_Counts[i];
            }
        }

        public void OnGUI(GuiEditor.Entry _)
        {
            // Sync only in the layout pass, so the layout and the following event see the same rows
            if (Event.current.type == EventType.Layout)
            {
                Sync();
            }

            EnsureStyles();

            m_TextFilter = GUILayout.TextField(m_TextFilter, m_TextFilterStyle, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));

            // Filters and Clear share the window width (do not grow with the text)
            using (new GUILayout.HorizontalScope())
            {
                for (var i = 0; i < LevelCount; i++)
                {
                    var previousColor = GUI.backgroundColor;
                    GUI.backgroundColor = m_Filters[i] ? ColorOf(i) : Color.gray;
                    var on = GUILayout.Toggle(m_Filters[i], m_FilterLabels[i], m_FilterStyle, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
                    GUI.backgroundColor = previousColor;
                    if (on != m_Filters[i])
                    {
                        m_Filters[i] = on;
                        RebuildFilterLabels();
                    }
                }

                if (GUILayout.Button("Clear", m_FilterStyle, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true)))
                {
                    Clear();
                }
            }

            if (m_Selected != null)
            {
                using (new GUILayout.VerticalScope(GUI.skin.box))
                {
                    m_DetailStyle!.normal.textColor = ColorOf(LevelOf(m_Selected.Type));
                    GUILayout.Label(m_DetailText, m_DetailStyle, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("Close", m_FilterStyle))
                    {
                        m_Selected = null;
                    }
                }
            }

            // Newest first. Rows fit the window width (a wide row would push the filters out of the window)
            for (var i = m_Snapshot.Count - 1; i >= 0; i--)
            {
                var entry = m_Snapshot[i];
                var level = LevelOf(entry.Type);
                if (!m_Filters[level] || entry.Message.IndexOf(m_TextFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (GUILayout.Button(entry.Header, m_RowStyles[level], GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true)))
                {
                    Select(entry);
                }
            }
        }

        private void Select(LogEntry entry)
        {
            if (ReferenceEquals(entry, m_Selected))
            {
                // Tap the same row again to close
                m_Selected = null;
                return;
            }

            m_Selected = entry;
            var text = entry.StackTrace.Length > 0
                ? entry.Message + "\n\n" + entry.StackTrace
                : entry.Message;
            m_DetailText = text.Length > MaxDetailLength ? text.Substring(0, MaxDetailLength) : text;
        }

        private Color ColorOf(int level)
        {
            return level switch
            {
                1 => m_Settings.WarningColor,
                2 => m_Settings.ErrorColor,
                _ => m_Settings.InfoColor,
            };
        }

        // GUI.skin can only be used inside OnGUI, so the styles are made here (once)
        private void EnsureStyles()
        {
            if (m_DetailStyle != null)
            {
                return;
            }

            for (var i = 0; i < LevelCount; i++)
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = m_Settings.FontSize,
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    wordWrap = false,
                    richText = false,
                };
                style.normal.textColor = ColorOf(i);
                m_RowStyles[i] = style;
            }

            m_DetailStyle = new GUIStyle(GUI.skin.label) { fontSize = m_Settings.FontSize, wordWrap = true, richText = false };
            m_FilterStyle = new GUIStyle(GUI.skin.button) { fontSize = m_Settings.FontSize + 1, clipping = TextClipping.Clip, richText = false };
            m_TextFilterStyle = new GUIStyle(GUI.skin.textField) { fontSize = m_Settings.FontSize + 1, richText = false };
        }
    }
}
