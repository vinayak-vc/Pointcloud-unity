using System;
using System.Collections.Generic;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using UnityEngine;

namespace ViitorCloud.PointForge {
    /// <summary>
    /// Streams a PointForge octree dataset and renders it with GPU buffers.
    ///
    /// Per frame: Unity camera state is sent to the native engine
    /// (PF_UpdateCamera), which runs frustum culling + screen-space-error LOD
    /// and streams newly visible nodes off disk on its worker thread. Finished
    /// loads are uploaded straight into per-node GraphicsBuffers (no Mesh, no
    /// GameObjects) and drawn with Graphics.RenderPrimitives. An LRU byte
    /// budget evicts nodes the camera moved away from.
    ///
    /// This transform is the octree cube centre. Native data is Z-up
    /// right-handed; ApplyRecommendedTransform() maps pf(x,y,z) -> unity(x,z,y)
    /// via rotation (-90,0,0) and scale (1,-1,1).
    /// </summary>
    /// <summary>Point coloring source. Values match _ColorMode in the PointForge/Points shader.</summary>
    public enum PointForgeColorMode {
        Rgb = 0,
        Elevation = 1,
        Intensity = 2,
        Classification = 3,
        LodLevel = 4
    }

    [ExecuteAlways]
    [AddComponentMenu("PointForge/Point Cloud")]
    public sealed class PointForgePointCloud : MonoBehaviour {
        private const int MaxUploadsHardCap = 64;

        [Header("Project")]
        [SerializeField] private string projectDirectory;
        [SerializeField] private bool openOnEnable = true;

        [Header("Rendering")]
        [SerializeField] private Material pointMaterial;
        [SerializeField, Range(1f, 32f)] private float pointSizePixels = 3f;
        [SerializeField] private PointForgeColorMode colorMode = PointForgeColorMode.Rgb;
        [SerializeField] private bool showBoundingBox = true;

        [Header("Streaming")]
        [SerializeField] private Camera targetCamera;
        [SerializeField, Range(1f, 32f)] private float sseBudgetPixels = 6f;
        [SerializeField, Range(64, 8192)] private int gpuBudgetMB = 1024;
        [SerializeField, Range(1, MaxUploadsHardCap)] private int maxUploadsPerFrame = 8;

        private PointForgeProject project;
        private readonly Dictionary<uint, NodeBuffer> residentBuffers = new Dictionary<uint, NodeBuffer>();
        private uint[] visibleScratch;
        private uint[] evictScratch;
        private Bounds cachedWorldBounds;
        private bool worldBoundsDirty = true;
        private bool warnedOrthographic;

        private static readonly int PointsBufferId = Shader.PropertyToID("_Points");
        private static readonly int LocalToWorldId = Shader.PropertyToID("_PointForgeLocalToWorld");
        private static readonly int PointSizeId = Shader.PropertyToID("_PointSize");
        private static readonly int ColorModeId = Shader.PropertyToID("_ColorMode");
        private static readonly int NodeLevelId = Shader.PropertyToID("_NodeLevel");
        private static readonly int ZBoundsId = Shader.PropertyToID("_ZBoundsPF");

        private sealed class NodeBuffer {
            public GraphicsBuffer Buffer;
            public int PointCount;
            public MaterialPropertyBlock Props;
        }

        public PointForgeProject Project {
            get { return project; }
        }

        public string ProjectDirectory {
            get { return projectDirectory; }
            set { projectDirectory = value; }
        }

        public Material PointMaterial {
            get { return pointMaterial; }
            set { pointMaterial = value; }
        }

        public float PointSizePixels {
            get { return pointSizePixels; }
            set { pointSizePixels = Mathf.Clamp(value, 1f, 32f); }
        }

        public float SseBudgetPixels {
            get { return sseBudgetPixels; }
            set { sseBudgetPixels = Mathf.Clamp(value, 1f, 32f); }
        }

        public int GpuBudgetMB {
            get { return gpuBudgetMB; }
            set { gpuBudgetMB = Mathf.Clamp(value, 64, 8192); }
        }

        public int MaxUploadsPerFrame {
            get { return maxUploadsPerFrame; }
            set { maxUploadsPerFrame = Mathf.Clamp(value, 1, MaxUploadsHardCap); }
        }

        public PointForgeColorMode ColorMode {
            get { return colorMode; }
            set { colorMode = value; }
        }

        public bool ShowBoundingBox {
            get { return showBoundingBox; }
            set { showBoundingBox = value; }
        }

        public int ResidentBufferCount {
            get { return residentBuffers.Count; }
        }

        private void OnEnable() {
            if (openOnEnable && !string.IsNullOrEmpty(projectDirectory)) {
                OpenProject(projectDirectory);
            }
        }

        private void OnDisable() {
            CloseProject();
        }

        /// <summary>Opens a converted PointForge dataset directory. Returns true on success.</summary>
        public bool OpenProject(string directory) {
            CloseProject();
            project = PointForgeProject.Open(directory);
            if (project == null) {
                return false;
            }
            projectDirectory = directory;
            visibleScratch = new uint[project.Metadata.NodeCount];
            evictScratch = new uint[Mathf.Min((int)project.Metadata.NodeCount, 512)];
            worldBoundsDirty = true;
            EnsureMaterial();
            return true;
        }

        public void CloseProject() {
            foreach (KeyValuePair<uint, NodeBuffer> entry in residentBuffers) {
                entry.Value.Buffer.Dispose();
            }
            residentBuffers.Clear();
            if (project != null) {
                project.Dispose();
                project = null;
            }
        }

        /// <summary>Sets this transform so PF Z-up right-handed maps to Unity Y-up: pf(x,y,z) -> unity(x,z,y).</summary>
        public void ApplyRecommendedTransform() {
            transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            transform.localScale = new Vector3(1f, -1f, 1f);
            worldBoundsDirty = true;
        }

        /// <summary>World-space bounds of the whole cloud (true AABB from metadata).</summary>
        public Bounds GetWorldBounds() {
            if (project == null) {
                return new Bounds(transform.position, Vector3.one);
            }
            if (worldBoundsDirty || transform.hasChanged) {
                cachedWorldBounds = ComputeWorldBounds();
                worldBoundsDirty = false;
                transform.hasChanged = false;
            }
            return cachedWorldBounds;
        }

        private void Update() {
            if (project == null || !project.IsOpen) {
                return;
            }
            Camera activeCamera = ResolveCamera();
            if (activeCamera != null) {
                StepStreaming(activeCamera);
            }
            RenderVisibleNodes();
        }

        private Camera ResolveCamera() {
            if (Application.isPlaying) {
                return targetCamera != null ? targetCamera : Camera.main;
            }
#if UNITY_EDITOR
            UnityEditor.SceneView sceneView = UnityEditor.SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null) {
                return sceneView.camera;
            }
#endif
            return targetCamera != null ? targetCamera : Camera.main;
        }

        private unsafe void StepStreaming(Camera activeCamera) {
            if (activeCamera.orthographic) {
                // The native screen-space-error metric assumes a perspective
                // projection; keep the last visibility set in ortho views.
                if (!warnedOrthographic) {
                    warnedOrthographic = true;
                    Debug.LogWarning("PointForge: orthographic cameras are not supported for LOD selection yet; streaming is paused while the view is orthographic.");
                }
            } else {
                warnedOrthographic = false;
                PointForgeNative.PFCameraState state = default(PointForgeNative.PFCameraState);
                Matrix4x4 localToWorld = transform.localToWorldMatrix;
                Matrix4x4 viewProj = activeCamera.projectionMatrix * activeCamera.worldToCameraMatrix * localToWorld;
                for (int i = 0; i < 16; i++) {
                    state.ViewProj[i] = viewProj[i]; // both sides column-major
                }
                Vector3 cameraPF = transform.InverseTransformPoint(activeCamera.transform.position);
                state.CameraPos[0] = cameraPF.x;
                state.CameraPos[1] = cameraPF.y;
                state.CameraPos[2] = cameraPF.z;
                state.FovYDegrees = activeCamera.fieldOfView;
                state.ViewportWidth = (uint)Mathf.Max(1, activeCamera.pixelWidth);
                state.ViewportHeight = (uint)Mathf.Max(1, activeCamera.pixelHeight);
                state.SseBudgetPixels = sseBudgetPixels;
                state.MaxLoadRequests = 0;
                project.UpdateCamera(ref state);
            }

            UploadFinishedLoads();
            EvictOverBudget();
        }

        private unsafe void UploadFinishedLoads() {
            int uploads = 0;
            PointForgeNative.PFLoadedNode loaded;
            while (uploads < maxUploadsPerFrame && project.TryDequeueLoadedNode(out loaded)) {
                if (loaded.PointCount == 0 || loaded.VertexData == IntPtr.Zero) {
                    project.ReleaseLoadedNode(loaded.NodeIndex, false);
                    continue;
                }

                GraphicsBuffer buffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    (int)loaded.PointCount,
                    PointForgeNative.VertexStride);

                int byteCount = (int)loaded.PointCount * PointForgeNative.VertexStride;
                NativeArray<byte> view = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                    (void*)loaded.VertexData, byteCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle safety = AtomicSafetyHandle.Create();
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref view, safety);
#endif
                buffer.SetData(view);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.Release(safety);
#endif
                // Native CPU copy can be freed now; node counts as GPU-resident.
                project.ReleaseLoadedNode(loaded.NodeIndex, true);

                NodeBuffer node = new NodeBuffer();
                node.Buffer = buffer;
                node.PointCount = (int)loaded.PointCount;
                node.Props = new MaterialPropertyBlock();
                node.Props.SetBuffer(PointsBufferId, buffer);
                PointForgeNative.PFNodeInfo nodeInfo;
                if (project.TryGetNodeInfo(loaded.NodeIndex, out nodeInfo)) {
                    node.Props.SetFloat(NodeLevelId, nodeInfo.Level);
                }
                residentBuffers[loaded.NodeIndex] = node;
                uploads++;
            }
        }

        private void EvictOverBudget() {
            if (evictScratch == null) {
                return;
            }
            ulong budgetBytes = (ulong)gpuBudgetMB * 1024ul * 1024ul;
            int evictCount = project.GetEvictionCandidates(budgetBytes, evictScratch);
            for (int i = 0; i < evictCount; i++) {
                uint nodeIndex = evictScratch[i];
                NodeBuffer node;
                if (residentBuffers.TryGetValue(nodeIndex, out node)) {
                    node.Buffer.Dispose();
                    residentBuffers.Remove(nodeIndex);
                }
                project.UnloadNode(nodeIndex);
            }
        }

        private void RenderVisibleNodes() {
            if (pointMaterial == null || visibleScratch == null) {
                return;
            }
            int visibleCount = project.GetVisibleNodes(visibleScratch);
            if (visibleCount == 0) {
                return;
            }

            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            Bounds worldBounds = GetWorldBounds();
            PointForgeNative.PFMetadata meta = project.Metadata;
            Vector3 cubeCenter = project.CubeCenterPF;
            Vector4 zBounds = new Vector4(
                (float)(meta.BbMinZ - cubeCenter.z),
                (float)(meta.BbMaxZ - meta.BbMinZ), 0f, 0f);

            for (int i = 0; i < visibleCount; i++) {
                NodeBuffer node;
                if (!residentBuffers.TryGetValue(visibleScratch[i], out node)) {
                    continue; // draw list and buffer map can briefly disagree during eviction
                }
                node.Props.SetMatrix(LocalToWorldId, localToWorld);
                node.Props.SetFloat(PointSizeId, pointSizePixels);
                node.Props.SetFloat(ColorModeId, (float)colorMode);
                node.Props.SetVector(ZBoundsId, zBounds);

                RenderParams renderParams = new RenderParams(pointMaterial);
                renderParams.matProps = node.Props;
                renderParams.worldBounds = worldBounds;
                renderParams.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderParams.receiveShadows = false;
                Graphics.RenderPrimitives(renderParams, MeshTopology.Triangles, node.PointCount * 6, 1);
            }
        }

        private void EnsureMaterial() {
            if (pointMaterial != null) {
                return;
            }
            Shader shader = Shader.Find("PointForge/Points");
            if (shader != null) {
                pointMaterial = new Material(shader);
                pointMaterial.name = "PointForgePoints (runtime)";
            } else {
                Debug.LogError("PointForge: shader 'PointForge/Points' not found; assign a material manually.");
            }
        }

        private Bounds ComputeWorldBounds() {
            PointForgeNative.PFMetadata meta = project.Metadata;
            Vector3 center = project.CubeCenterPF;
            Vector3 minPF = new Vector3((float)meta.BbMinX, (float)meta.BbMinY, (float)meta.BbMinZ) - center;
            Vector3 maxPF = new Vector3((float)meta.BbMaxX, (float)meta.BbMaxY, (float)meta.BbMaxZ) - center;

            Bounds bounds = new Bounds(transform.TransformPoint((minPF + maxPF) * 0.5f), Vector3.zero);
            for (int corner = 0; corner < 8; corner++) {
                Vector3 local = new Vector3(
                    (corner & 1) != 0 ? maxPF.x : minPF.x,
                    (corner & 2) != 0 ? maxPF.y : minPF.y,
                    (corner & 4) != 0 ? maxPF.z : minPF.z);
                bounds.Encapsulate(transform.TransformPoint(local));
            }
            return bounds;
        }

        private void OnDrawGizmos() {
            if (!showBoundingBox || project == null || !project.IsOpen) {
                return;
            }
            Bounds bounds = GetWorldBounds();
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 1f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
