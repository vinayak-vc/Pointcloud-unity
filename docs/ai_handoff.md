# AI Handoff — Pointcloud-unity

## Latest Session (2026-07-02) — Initial PointForge → Unity integration

Implemented Phases 1–3 (and parts of 4) of the integration plan: PointForge
compiled as a native DLL, consumed by a new Unity plugin in this repo.
Read docs/architecture.md first; decisions.md explains every non-obvious
choice (D1–D8).

### PointForge repo side (separate repo!)
`C:\UnrealProject\PointForge`, branch **`library/unity`**, commit `2965fc8`:
- `src/library/unity/PointForgeC.h/.cpp` — flat C API (PF_OpenProject,
  PF_UpdateCamera, PF_GetVisibleNodes, PF_DequeueLoadedNode,
  PF_ReleaseLoadedNode, PF_UnloadNode, PF_GetEvictionCandidates,
  PF_GetStatistics, PF_GetNodeInfo, PF_SetLogCallback, PF_GetVersion,
  PF_CloseProject). pfcore untouched.
- CMake target `pfunity` (option `PF_BUILD_UNITY_PLUGIN`, default ON).
- Build used:
  `cmake --build build-static --config Release --target pfunity`
  (static triplet; output `build-static/Release/PointForgeUnity.dll`).
- Smoke-tested natively against `C:\UnrealProject\model\PointForgeCache_direct`
  (1024 nodes, 12.4M points): traversal, streaming, stats, eviction all OK.

### This repo — files created
- `Plugins/x86_64/PointForgeUnity.dll` (copy of the build above)
- `Runtime/PointForge.Runtime.asmdef` (allowUnsafeCode)
- `Runtime/Native/PointForgeNative.cs` — P/Invoke, POD structs (keep in sync
  with PointForgeC.h)
- `Runtime/PointForgeProject.cs` — handle wrapper, native log pump
- `Runtime/PointForgePointCloud.cs` — [ExecuteAlways] streaming manager +
  Graphics.RenderPrimitives renderer, per-node GraphicsBuffers, LRU eviction
- `Runtime/Shaders/PointForgePoints.shader` — URP opaque point-quad shader
- `Editor/PointForge.Editor.asmdef`, `Editor/PointForgeWindow.cs` — menu
  "PointForge > Viewer", panels Project/Scene View/Statistics/Rendering/
  Streaming/Console
- `docs/*` — this documentation set

### Verification state — IMPORTANT
The native DLL is verified (standalone smoke test). The **Unity side has NOT
been compiled or run inside the Unity editor this session** (files written
externally). First action next session:
1. Open the project in Unity 6000.3.9f1, let it compile; fix any compile
   errors (most likely spots: `AOT.MonoPInvokeCallback` attribute,
   `Marshal.PtrToStringUTF8`, `FindFirstObjectByType` availability, shader
   include path).
2. PointForge > Viewer → open `C:\UnrealProject\model\PointForgeCache_direct`.
3. Confirm: metadata panel populates, orange bbox gizmo visible, points render
   in Scene view, Statistics update as the camera moves.

### Known gaps
- No DepthOnly/DepthNormals pass (URP SSAO/shadows/depth prepass blind to points).
- Orthographic scene cameras pause streaming (native SSE is perspective-only).
- No buffer pooling yet; GraphicsBuffers are created/disposed per node churn.
- LOD visualization + intensity/classification color modes pending (data
  already sits in the vertex buffer's `packedExtra`).

### Next recommended task
In-editor verification (above), then LOD-level debug coloring (add
`_ColorMode` to the shader; level can be passed per node via the
MaterialPropertyBlock at upload time).
