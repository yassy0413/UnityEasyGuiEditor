using System;
using UnityEngine;
using UnityEngine.Profiling;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEasyGuiEditor
{
    [DisallowMultipleComponent]
    public sealed class GuiEditorSample : MonoBehaviour
    {
        private const double MiB = 1024 * 1024;

        [SerializeField]
        private bool m_EditorSample = true;

        [SerializeField]
        private bool m_CanvasSample = true;

        private void Start()
        {
            if (m_EditorSample)
            {
                AddEditorSample();
            }

            if (m_CanvasSample)
            {
                AddCanvasSample();
            }
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(1))
            {
                GuiEditor.Instance.enabled = !GuiEditor.Instance.enabled;
            }
        }

        private void AddEditorSample()
        {
            GuiEditor.AddEntry("System", entry =>
            {
                const int LabelWidth = 120;
                const int SliderHeight = 16;
                {
                    using var _hs = new GUILayout.HorizontalScope(GUI.skin.box);
                    GUILayout.Label("Time Scale", GUILayout.Width(LabelWidth));

                    using var _vs = new GUILayout.VerticalScope();
                    var v0 = Time.timeScale;
                    var v1 = GUILayout.HorizontalSlider(v0, 0.1f, 4.0f, GUILayout.Height(SliderHeight));
                    if (!Mathf.Approximately(v0, v1))
                    {
                        Time.timeScale = v1;
                    }
                    GUILayout.Label(v0.ToString());
                }
                {
                    using var _hs = new GUILayout.HorizontalScope(GUI.skin.box);
                    GUILayout.Label("VSYNC Count", GUILayout.Width(LabelWidth));

                    using var _vs = new GUILayout.VerticalScope();
                    var v0 = QualitySettings.vSyncCount;
                    var v1 = (int)GUILayout.HorizontalSlider(v0, 0, 4, GUILayout.Height(SliderHeight));
                    if (v0 != v1)
                    {
                        QualitySettings.vSyncCount = v1;
                    }
                    GUILayout.Label(v0.ToString());
                }
                {
                    using var _hs = new GUILayout.HorizontalScope(GUI.skin.box);
                    GUILayout.Label("FPS", GUILayout.Width(LabelWidth));

                    using var _vs = new GUILayout.VerticalScope();
                    var v0 = Application.targetFrameRate;
                    var v1 = (int)GUILayout.HorizontalSlider(v0, -1, 60, GUILayout.Height(SliderHeight));
                    if (v0 != v1)
                    {
                        Application.targetFrameRate = v1;
                    }
                    GUILayout.Label(v0.ToString());
                }
                if (UnityEngine.Scripting.GarbageCollector.isIncremental)
                {
                    using var _hs = new GUILayout.HorizontalScope(GUI.skin.box);
                    GUILayout.Label("IGC TimeSlice(msec)", GUILayout.Width(LabelWidth));

                    using var _vs = new GUILayout.VerticalScope();
                    var v0 = (int)(UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds / 1000000);
                    var v1 = (int)GUILayout.HorizontalSlider(v0, 1, 1000, GUILayout.Height(SliderHeight));
                    if (v0 != v1)
                    {//1000000 nano => 1 milli
                        UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds = (ulong)(v1 * 1000000);
                    }
                    GUILayout.Label(v0.ToString());
                }
            });

            GuiEditor.AddEntry("Permission", entry =>
            {
                foreach (UserAuthorization ua in Enum.GetValues(typeof(UserAuthorization)))
                {
                    GUILayout.Label($"[{ua}]");

                    if (Application.HasUserAuthorization(ua))
                    {
                        GUILayout.Label("Authorized.");
                        continue;
                    }

                    if (GUILayout.Button("Request"))
                    {
                        _ = Application.RequestUserAuthorization(ua);
                    }
                }
            });

            var _frameTimings = new FrameTiming[1];
            GuiEditor.AddEntry("Profile", entry =>
            {
                var cpuFrameTime = 0.0;
                var gpuFrameTime = 0.0;

                FrameTimingManager.CaptureFrameTimings();
                var frames = FrameTimingManager.GetLatestTimings((uint)_frameTimings.Length, _frameTimings);
                if (frames > 0)
                {
                    cpuFrameTime = _frameTimings[0].cpuFrameTime;
                    gpuFrameTime = _frameTimings[0].gpuFrameTime;
                }
                else
                {
#if UNITY_EDITOR
                    cpuFrameTime = UnityStats.frameTime * 1000;
                    gpuFrameTime = UnityStats.renderTime * 1000;
#endif
                }

                GUILayout.Label($"CPU: {cpuFrameTime:F1}");
                GUILayout.Label($"GPU: {gpuFrameTime:F1}");

                GUILayout.Label(string.Format("Reserved  {0:F1}MiB", Profiler.GetTotalReservedMemoryLong() / MiB));
                GUILayout.Label(string.Format("Allocated {0:F1}MiB", Profiler.GetTotalAllocatedMemoryLong() / MiB));
                GUILayout.Label(string.Format("Unused    {0:F1}MiB", Profiler.GetTotalUnusedReservedMemoryLong() / MiB));
                GUILayout.Label(string.Format("Graphics  {0:F1}MiB", Profiler.GetAllocatedMemoryForGraphicsDriver() / MiB));
                GUILayout.Label(string.Format("Mono Used {0:F1}MiB", Profiler.GetMonoUsedSizeLong() / MiB));
                GUILayout.Label(string.Format("Mono Heap {0:F1}MiB", Profiler.GetMonoHeapSizeLong() / MiB));
            });

            var entry = GuiEditor.Instance.GetEntry("Sample/Test1/Test2/Test3/Test4/Test5");
            entry.Add("Test", entry =>
            {
                GUILayout.Label(entry.Name);
            });
        }

        private void AddCanvasSample()
        {
            GuiCanvas.OnGui += rc =>
            {
                var width = 220;
                var rect = new Rect(20, 100, width, 60);
                using var _as = new GUILayout.AreaScope(rect, string.Empty, GUI.skin.box);
                using var _vs = new GUILayout.VerticalScope();
                GUILayout.Label(DateTimeOffset.UtcNow.ToString());
                GUILayout.Label(DateTimeOffset.UtcNow.ToLocalTime().ToString());
            };
        }
    }
}
