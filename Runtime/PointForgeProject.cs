using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using UnityEngine;

namespace ViitorCloud.PointForge {
    /// <summary>
    /// Managed lifetime wrapper around a native PFProject handle (an opened
    /// PointForge octree directory: meta.bin / hierarchy.bin / octree.bin).
    /// Owns the handle; dispose to stop the native streaming worker.
    /// Main-thread only, mirroring the native contract.
    /// </summary>
    public sealed class PointForgeProject : IDisposable {
        private IntPtr handle;
        private PointForgeNative.PFMetadata metadata;

        // Native log lines arrive on the streaming worker thread; they are
        // buffered here and drained on the main thread (editor Console panel).
        private static readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        private static PointForgeNative.PFLogCallback logCallback; // kept alive against GC
        private static bool logCaptureActive;

        public string Directory { get; private set; }

        public PointForgeNative.PFMetadata Metadata {
            get { return metadata; }
        }

        public bool IsOpen {
            get { return handle != IntPtr.Zero; }
        }

        public Vector3 CubeCenterPF {
            get {
                return new Vector3(
                    (float)(metadata.CubeMinX + metadata.CubeSize * 0.5),
                    (float)(metadata.CubeMinY + metadata.CubeSize * 0.5),
                    (float)(metadata.CubeMinZ + metadata.CubeSize * 0.5));
            }
        }

        private PointForgeProject(IntPtr handle, string directory) {
            this.handle = handle;
            Directory = directory;
            PointForgeNative.PF_GetMetadata(handle, out metadata);
        }

        /// <summary>Opens a converted PointForge dataset directory. Returns null on failure.</summary>
        public static PointForgeProject Open(string directory) {
            if (string.IsNullOrEmpty(directory)) {
                Debug.LogError("PointForge: project directory is empty.");
                return null;
            }
            EnsureLogCapture();
            IntPtr nativeHandle = PointForgeNative.PF_OpenProject(directory);
            if (nativeHandle == IntPtr.Zero) {
                Debug.LogError($"PointForge: failed to open project at '{directory}' (missing or invalid meta.bin/hierarchy.bin/octree.bin).");
                return null;
            }
            return new PointForgeProject(nativeHandle, directory);
        }

        public void Dispose() {
            if (handle != IntPtr.Zero) {
                PointForgeNative.PF_CloseProject(handle);
                handle = IntPtr.Zero;
            }
        }

        public int UpdateCamera(ref PointForgeNative.PFCameraState camera) {
            if (handle == IntPtr.Zero) {
                return -1;
            }
            return PointForgeNative.PF_UpdateCamera(handle, ref camera);
        }

        public unsafe int GetVisibleNodes(uint[] outIndices) {
            if (handle == IntPtr.Zero || outIndices == null || outIndices.Length == 0) {
                return 0;
            }
            fixed (uint* ptr = outIndices) {
                return PointForgeNative.PF_GetVisibleNodes(handle, ptr, outIndices.Length);
            }
        }

        public bool TryDequeueLoadedNode(out PointForgeNative.PFLoadedNode loadedNode) {
            if (handle == IntPtr.Zero) {
                loadedNode = default(PointForgeNative.PFLoadedNode);
                return false;
            }
            return PointForgeNative.PF_DequeueLoadedNode(handle, out loadedNode) != 0;
        }

        public void ReleaseLoadedNode(uint nodeIndex, bool uploadedToGpu) {
            if (handle != IntPtr.Zero) {
                PointForgeNative.PF_ReleaseLoadedNode(handle, nodeIndex, uploadedToGpu ? 1 : 0);
            }
        }

        public void UnloadNode(uint nodeIndex) {
            if (handle != IntPtr.Zero) {
                PointForgeNative.PF_UnloadNode(handle, nodeIndex);
            }
        }

        public unsafe int GetEvictionCandidates(ulong budgetBytes, uint[] outIndices) {
            if (handle == IntPtr.Zero || outIndices == null || outIndices.Length == 0) {
                return 0;
            }
            fixed (uint* ptr = outIndices) {
                return PointForgeNative.PF_GetEvictionCandidates(handle, budgetBytes, ptr, outIndices.Length);
            }
        }

        public PointForgeNative.PFStatistics GetStatistics() {
            PointForgeNative.PFStatistics statistics = default(PointForgeNative.PFStatistics);
            if (handle != IntPtr.Zero) {
                PointForgeNative.PF_GetStatistics(handle, out statistics);
            }
            return statistics;
        }

        public bool TryGetNodeInfo(uint nodeIndex, out PointForgeNative.PFNodeInfo nodeInfo) {
            nodeInfo = default(PointForgeNative.PFNodeInfo);
            if (handle == IntPtr.Zero) {
                return false;
            }
            return PointForgeNative.PF_GetNodeInfo(handle, nodeIndex, out nodeInfo) != 0;
        }

        /// <summary>Drains buffered native log lines; call from the main thread.</summary>
        public static int DrainLogs(Action<string> onLine) {
            int drained = 0;
            string line;
            while (logQueue.TryDequeue(out line)) {
                if (onLine != null) {
                    onLine(line);
                }
                drained++;
            }
            return drained;
        }

        private static void EnsureLogCapture() {
            if (logCaptureActive) {
                return;
            }
            logCallback = OnNativeLog;
            PointForgeNative.PF_SetLogCallback(logCallback);
            logCaptureActive = true;
        }

        [AOT.MonoPInvokeCallback(typeof(PointForgeNative.PFLogCallback))]
        private static void OnNativeLog(IntPtr messageUtf8) {
            // Worker-thread context: only marshal + enqueue, no Unity API.
            string line = Marshal.PtrToStringUTF8(messageUtf8);
            if (line != null) {
                logQueue.Enqueue(line);
                while (logQueue.Count > 2048) {
                    logQueue.TryDequeue(out _);
                }
            }
        }
    }
}
