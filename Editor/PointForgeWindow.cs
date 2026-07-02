using System.Collections.Generic;

using UnityEditor;

using UnityEngine;

namespace ViitorCloud.PointForge.Editor {
    /// <summary>
    /// PointForge editor window: open converted octree datasets, inspect
    /// metadata and live streaming statistics, and tune rendering/streaming
    /// parameters of the PointForgePointCloud component in the scene.
    /// Conversion of raw scans (LAS/E57/...) is intentionally NOT done here —
    /// use the PointForge pfconvert CLI, then open the output directory.
    /// </summary>
    public sealed class PointForgeWindow : EditorWindow {
        private static readonly string[] TabNames = {
            "Project", "Scene View", "Statistics", "Rendering", "Streaming", "Console"
        };

        private int activeTab;
        private string directoryInput = string.Empty;
        private Vector2 consoleScroll;
        private Vector2 mainScroll;
        private bool autoScrollConsole = true;
        private readonly List<string> consoleLines = new List<string>(1024);
        private PointForgePointCloud cloud;

        [MenuItem("PointForge/Viewer")]
        public static void OpenWindow() {
            PointForgeWindow window = GetWindow<PointForgeWindow>("PointForge");
            window.minSize = new Vector2(380f, 420f);
            window.Show();
        }

        private void OnEnable() {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable() {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate() {
            int drained = PointForgeProject.DrainLogs(AppendConsoleLine);
            bool projectOpen = cloud != null && cloud.Project != null && cloud.Project.IsOpen;
            if (projectOpen) {
                // Keep streaming alive while the editor is idle: the component
                // only steps inside Update(), which needs scene repaints.
                SceneView.RepaintAll();
            }
            if (drained > 0 || projectOpen) {
                Repaint();
            }
        }

        private void AppendConsoleLine(string line) {
            consoleLines.Add(line);
            if (consoleLines.Count > 2048) {
                consoleLines.RemoveRange(0, consoleLines.Count - 2048);
            }
        }

        private void OnGUI() {
            ResolveCloud();
            activeTab = GUILayout.Toolbar(activeTab, TabNames);
            EditorGUILayout.Space(4f);
            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            switch (activeTab) {
                case 0: DrawProjectTab(); break;
                case 1: DrawSceneViewTab(); break;
                case 2: DrawStatisticsTab(); break;
                case 3: DrawRenderingTab(); break;
                case 4: DrawStreamingTab(); break;
                case 5: DrawConsoleTab(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void ResolveCloud() {
            if (cloud == null) {
                cloud = FindFirstObjectByType<PointForgePointCloud>();
            }
        }

        private bool IsProjectOpen() {
            return cloud != null && cloud.Project != null && cloud.Project.IsOpen;
        }

        // ---- Project ------------------------------------------------------

        private void DrawProjectTab() {
            EditorGUILayout.LabelField("PointForge Dataset", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Open a directory produced by pfconvert (contains meta.bin, hierarchy.bin, octree.bin).",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            directoryInput = EditorGUILayout.TextField("Directory", directoryInput);
            if (GUILayout.Button("Browse", GUILayout.Width(70f))) {
                string picked = EditorUtility.OpenFolderPanel("Open PointForge dataset", directoryInput, string.Empty);
                if (!string.IsNullOrEmpty(picked)) {
                    directoryInput = picked;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(directoryInput))) {
                if (GUILayout.Button("Open Project")) {
                    OpenProject();
                }
            }
            using (new EditorGUI.DisabledScope(!IsProjectOpen())) {
                if (GUILayout.Button("Close Project")) {
                    cloud.CloseProject();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            if (cloud == null) {
                EditorGUILayout.HelpBox("No PointForgePointCloud component in the scene. Opening a project creates one.", MessageType.None);
                return;
            }

            EditorGUILayout.ObjectField("Scene Component", cloud, typeof(PointForgePointCloud), true);

            if (!IsProjectOpen()) {
                EditorGUILayout.HelpBox("No project open.", MessageType.None);
                return;
            }

            PointForgeNative.PFMetadata meta = cloud.Project.Metadata;
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Metadata", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Directory", cloud.Project.Directory);
            EditorGUILayout.LabelField("Points", meta.PointCount.ToString("N0"));
            EditorGUILayout.LabelField("Octree Nodes", meta.NodeCount.ToString("N0"));
            EditorGUILayout.LabelField("Root Spacing", meta.RootSpacing.ToString("F3") + " m");
            EditorGUILayout.LabelField("Cube Size", meta.CubeSize.ToString("F2") + " m");
            EditorGUILayout.LabelField("Bounds Min", FormatVector(meta.BbMinX, meta.BbMinY, meta.BbMinZ));
            EditorGUILayout.LabelField("Bounds Max", FormatVector(meta.BbMaxX, meta.BbMaxY, meta.BbMaxZ));
            EditorGUILayout.LabelField("Color", meta.HasColor != 0 ? "yes" : "no");
            EditorGUILayout.LabelField("Classification", meta.HasClassification != 0 ? "yes" : "no");
            EditorGUILayout.LabelField("Compression", meta.CompressionType == 1 ? "zstd per node" : "none");
        }

        private void OpenProject() {
            if (cloud == null) {
                GameObject host = new GameObject("PointForge Point Cloud");
                Undo.RegisterCreatedObjectUndo(host, "Create PointForge Point Cloud");
                cloud = host.AddComponent<PointForgePointCloud>();
                cloud.ApplyRecommendedTransform();
            }
            if (cloud.OpenProject(directoryInput)) {
                AppendConsoleLine($"[editor] opened '{directoryInput}'");
            } else {
                AppendConsoleLine($"[editor] FAILED to open '{directoryInput}'");
            }
        }

        // ---- Scene View -----------------------------------------------------

        private void DrawSceneViewTab() {
            EditorGUILayout.LabelField("Scene View", EditorStyles.boldLabel);
            if (!IsProjectOpen()) {
                EditorGUILayout.HelpBox("Open a project first.", MessageType.None);
                return;
            }

            if (GUILayout.Button("Frame Point Cloud")) {
                SceneView view = SceneView.lastActiveSceneView;
                if (view != null) {
                    view.Frame(cloud.GetWorldBounds(), false);
                }
            }
            if (GUILayout.Button("Select Component")) {
                Selection.activeGameObject = cloud.gameObject;
                EditorGUIUtility.PingObject(cloud.gameObject);
            }
            if (GUILayout.Button("Apply Recommended Transform (Z-up → Y-up)")) {
                Undo.RecordObject(cloud.transform, "PointForge transform");
                cloud.ApplyRecommendedTransform();
            }

            EditorGUILayout.Space(4f);
            cloud.ShowBoundingBox = EditorGUILayout.Toggle("Show Bounding Box", cloud.ShowBoundingBox);

            Bounds bounds = cloud.GetWorldBounds();
            EditorGUILayout.LabelField("World Center", bounds.center.ToString("F1"));
            EditorGUILayout.LabelField("World Size", bounds.size.ToString("F1"));
        }

        // ---- Statistics ------------------------------------------------------

        private void DrawStatisticsTab() {
            EditorGUILayout.LabelField("Streaming Statistics", EditorStyles.boldLabel);
            if (!IsProjectOpen()) {
                EditorGUILayout.HelpBox("Open a project first.", MessageType.None);
                return;
            }

            PointForgeNative.PFStatistics stats = cloud.Project.GetStatistics();
            EditorGUILayout.LabelField("Frame", stats.FrameIndex.ToString("N0"));
            EditorGUILayout.LabelField("Visible Nodes", stats.VisibleNodeCount.ToString("N0"));
            EditorGUILayout.LabelField("Renderable Nodes", stats.RenderableNodeCount.ToString("N0"));
            EditorGUILayout.LabelField("Resident Nodes", stats.ResidentNodeCount.ToString("N0"));
            EditorGUILayout.LabelField("Pending Disk Loads", stats.PendingLoadCount.ToString("N0"));
            EditorGUILayout.LabelField("Awaiting Upload", stats.AwaitingUploadCount.ToString("N0"));
            EditorGUILayout.LabelField("Points On GPU", stats.ResidentPointCount.ToString("N0"));
            EditorGUILayout.LabelField("GPU Bytes", FormatBytes(stats.ResidentByteCount));
            EditorGUILayout.LabelField("Unity Buffers", cloud.ResidentBufferCount.ToString("N0"));
        }

        // ---- Rendering -------------------------------------------------------

        private void DrawRenderingTab() {
            EditorGUILayout.LabelField("Rendering", EditorStyles.boldLabel);
            if (cloud == null) {
                EditorGUILayout.HelpBox("Open a project first.", MessageType.None);
                return;
            }

            Undo.RecordObject(cloud, "PointForge rendering settings");
            cloud.PointSizePixels = EditorGUILayout.Slider("Point Size (px)", cloud.PointSizePixels, 1f, 32f);
            cloud.ColorMode = (PointForgeColorMode)EditorGUILayout.EnumPopup(
                new GUIContent("Color Mode", "LOD Level tints each octree node by its depth — useful to visualize streaming/LOD behavior."),
                cloud.ColorMode);
            cloud.PointMaterial = (Material)EditorGUILayout.ObjectField("Material", cloud.PointMaterial, typeof(Material), false);
            EditorGUILayout.HelpBox(
                "Points are opaque screen-aligned quads with depth write/test (shader 'PointForge/Points'). No Mesh objects are created; nodes render from GraphicsBuffers via Graphics.RenderPrimitives.",
                MessageType.Info);
        }

        // ---- Streaming -------------------------------------------------------

        private void DrawStreamingTab() {
            EditorGUILayout.LabelField("Streaming", EditorStyles.boldLabel);
            if (cloud == null) {
                EditorGUILayout.HelpBox("Open a project first.", MessageType.None);
                return;
            }

            Undo.RecordObject(cloud, "PointForge streaming settings");
            cloud.SseBudgetPixels = EditorGUILayout.Slider(
                new GUIContent("SSE Budget (px)", "Descend the octree while a node's projected point spacing exceeds this many pixels. Lower = more detail = more GPU memory."),
                cloud.SseBudgetPixels, 1f, 32f);
            cloud.GpuBudgetMB = EditorGUILayout.IntSlider(
                new GUIContent("GPU Budget (MB)", "LRU eviction target for resident node buffers."),
                cloud.GpuBudgetMB, 64, 8192);
            cloud.MaxUploadsPerFrame = EditorGUILayout.IntSlider(
                new GUIContent("Uploads / Frame", "Maximum node buffers uploaded to the GPU per frame."),
                cloud.MaxUploadsPerFrame, 1, 64);

            if (IsProjectOpen()) {
                PointForgeNative.PFStatistics stats = cloud.Project.GetStatistics();
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Pending Disk Loads", stats.PendingLoadCount.ToString("N0"));
                EditorGUILayout.LabelField("GPU Usage", FormatBytes(stats.ResidentByteCount) + " / " + cloud.GpuBudgetMB + " MB");
            }
        }

        // ---- Console --------------------------------------------------------

        private void DrawConsoleTab() {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Native Engine Log", EditorStyles.boldLabel);
            autoScrollConsole = GUILayout.Toggle(autoScrollConsole, "Auto-scroll", GUILayout.Width(90f));
            if (GUILayout.Button("Clear", GUILayout.Width(60f))) {
                consoleLines.Clear();
            }
            EditorGUILayout.EndHorizontal();

            if (autoScrollConsole) {
                consoleScroll.y = float.MaxValue;
            }
            consoleScroll = EditorGUILayout.BeginScrollView(consoleScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < consoleLines.Count; i++) {
                EditorGUILayout.LabelField(consoleLines[i], EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        // ---- helpers --------------------------------------------------------

        private static string FormatVector(double x, double y, double z) {
            return $"({x:F2}, {y:F2}, {z:F2})";
        }

        private static string FormatBytes(ulong bytes) {
            if (bytes >= 1024ul * 1024ul * 1024ul) {
                return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
            }
            if (bytes >= 1024ul * 1024ul) {
                return (bytes / (1024.0 * 1024.0)).ToString("F1") + " MB";
            }
            return (bytes / 1024.0).ToString("F1") + " KB";
        }
    }
}
