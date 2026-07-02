# Architecture — Pointcloud-unity

```
PointForge.Core (C++, repo C:\UnrealProject\PointForge, branch library/unity)
        │  pfunity target = OctreeStore + Log + PointForgeC (flat C API)
Native DLL (PointForgeUnity.dll — POD-only boundary, KERNEL32-only deps)
        │  P/Invoke (PointForgeNative.cs)
Unity C# Wrapper (PointForgeProject.cs — handle lifetime, log pump)
        │
Streaming Manager (PointForgePointCloud.cs — camera sync, uploads, eviction)
        │
GraphicsBuffer (one Structured buffer per resident octree node, stride 20)
        │
URP Shader (PointForge/Points — vertex pulls buffer, expands point → quad)
        │
Unity Viewport (Graphics.RenderPrimitives, no Mesh, no GameObjects per node)
```

## Division of responsibility
Native engine decides **what** to draw (frustum cull + screen-space-error LOD
+ async disk streaming, identical code path to the desktop pfview viewer).
Unity decides **how** to draw it (GPU buffers + URP shader) and owns all GPU
memory. Residency is mirrored across the boundary:

1. `PF_UpdateCamera(viewProj, camPos, viewport, fov, sseBudget)` each frame —
   native traverses the octree, requests loads for visible non-resident nodes
   on its worker thread, refreshes the draw list.
2. `PF_DequeueLoadedNode` → pointer to `pointCount * 20B` vertices. Unity
   copies straight into a `GraphicsBuffer` (NativeArray view over the native
   pointer, zero managed allocations) and calls
   `PF_ReleaseLoadedNode(uploaded=true)` → node becomes draw-listed.
3. `PF_GetVisibleNodes` → draw list (visible ∧ resident). One
   `Graphics.RenderPrimitives(topology=Triangles, count = points*6)` per node.
4. `PF_GetEvictionCandidates(budget)` → LRU list (never nodes in the current
   draw list). Unity disposes those buffers and calls `PF_UnloadNode`.

## Coordinate mapping
Native space = "centred PF": metres relative to the octree cube centre, Z-up,
right-handed (exactly the float coordinates pfview uploads to OpenGL). The
`PointForgePointCloud` transform *is* the cube centre; the recommended
transform (rotation (-90,0,0), scale (1,-1,1)) maps pf(x,y,z) → unity(x,z,y)
with no chirality flip. Camera state is transformed into centred PF space in
C# (`proj * view * localToWorld`; `InverseTransformPoint` for the position),
so the native frustum/SSE code runs unchanged.

## Vertex format (20 bytes, native `pf::GpuVertex` passthrough)
```
float3 position   (centred PF space)
uint   rgba       (byte0=r .. byte3=a)
uint   extra      (bits 0-15 intensity, 16-23 classification)
```
StructuredBuffer stride 20 (multiple of 4). No repacking anywhere.

## Shader
`PointForge/Points`: opaque, `ZWrite On`, `ZTest LEqual`, `Blend Off`,
`Cull Off`. Each point expands to a screen-aligned quad (6 vertices via
`SV_VertexID`, fixed pixel size in clip space). Vertex color from the packed
rgba. No transparency by design. Known gap: no DepthOnly/DepthNormals pass
yet, so URP depth prepass, SSAO and shadows do not see the points.

## Threading
All API calls happen on the Unity main thread. Disk I/O runs on the native
worker thread inside OctreeStore. The native log callback fires on that
worker thread → `PointForgeProject` only enqueues into a ConcurrentQueue,
drained by the editor window on the main thread.

## Editor
`PointForgeWindow` ("PointForge" main menu): Project / Scene View /
Statistics / Rendering / Streaming / Console panels. It operates on the
single `PointForgePointCloud` component in the scene (created on first open).
Edit-mode streaming works because the component is `[ExecuteAlways]` and the
window pumps `SceneView.RepaintAll()` while a project is open.
