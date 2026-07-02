using System;
using System.Runtime.InteropServices;

namespace ViitorCloud.PointForge {
    /// <summary>
    /// Raw P/Invoke bindings for PointForgeUnity.dll (PointForge C API, version 1).
    /// All structs are POD mirrors of src/library/unity/PointForgeC.h in the
    /// PointForge repository — keep both sides in sync.
    /// All functions must be called from the Unity main thread; disk streaming
    /// runs on the native worker thread internally.
    /// </summary>
    public static class PointForgeNative {
        private const string DllName = "PointForgeUnity";

        /// <summary>Native log callback. Invoked from arbitrary native threads — do not touch Unity API inside.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void PFLogCallback(IntPtr messageUtf8);

        [StructLayout(LayoutKind.Sequential)]
        public struct PFMetadata {
            public ulong PointCount;
            public double BbMinX;
            public double BbMinY;
            public double BbMinZ;
            public double BbMaxX;
            public double BbMaxY;
            public double BbMaxZ;
            public double CubeMinX;
            public double CubeMinY;
            public double CubeMinZ;
            public double CubeSize;
            public double RootSpacing;
            public uint NodeCount;
            public uint RootNodeIndex;
            public uint HasColor;
            public uint HasClassification;
            public uint CompressionType;
            public uint BytesPerPoint;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PFNodeInfo {
            public uint Index;
            public uint PointCount;
            public uint Level;
            public uint ChildMask;
            public float MinX;
            public float MinY;
            public float MinZ;
            public float Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct PFCameraState {
            public fixed float ViewProj[16];
            public fixed float CameraPos[3];
            public float FovYDegrees;
            public uint ViewportWidth;
            public uint ViewportHeight;
            public float SseBudgetPixels;
            public uint MaxLoadRequests;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PFLoadedNode {
            public uint NodeIndex;
            public uint PointCount;
            public IntPtr VertexData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PFStatistics {
            public ulong FrameIndex;
            public uint VisibleNodeCount;
            public uint RenderableNodeCount;
            public uint ResidentNodeCount;
            public uint PendingLoadCount;
            public uint AwaitingUploadCount;
            public uint Pad;
            public ulong ResidentPointCount;
            public ulong ResidentByteCount;
        }

        /// <summary>Bytes per streamed vertex (float3 position + rgba + intensity/classification).</summary>
        public const int VertexStride = 20;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint PF_GetVersion();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern IntPtr PF_OpenProject([MarshalAs(UnmanagedType.LPUTF8Str)] string directoryUtf8);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void PF_CloseProject(IntPtr project);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PF_GetMetadata(IntPtr project, out PFMetadata metadata);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PF_GetNodeInfo(IntPtr project, uint nodeIndex, out PFNodeInfo nodeInfo);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PF_UpdateCamera(IntPtr project, ref PFCameraState camera);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int PF_GetVisibleNodes(IntPtr project, uint* outIndices, int capacity);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PF_DequeueLoadedNode(IntPtr project, out PFLoadedNode loadedNode);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void PF_ReleaseLoadedNode(IntPtr project, uint nodeIndex, int uploadedToGpu);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void PF_UnloadNode(IntPtr project, uint nodeIndex);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int PF_GetEvictionCandidates(IntPtr project, ulong budgetBytes, uint* outIndices, int capacity);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void PF_GetStatistics(IntPtr project, out PFStatistics statistics);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void PF_SetLogCallback(PFLogCallback callback);
    }
}
