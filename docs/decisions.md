# Decisions — Pointcloud-unity

## D1: Native DLL contains the streaming reader only, not full pfcore
`pfunity` compiles `OctreeStore.cpp + Log.cpp + PointForgeC.cpp` and links
glm + zstd only. **Why:** Unity consumes converted octrees; the importer
(laszip/E57 readers, indexer) is not needed and would drag in extra DLLs.
pfcore itself is untouched (requirement: treat PointForge as an external SDK).
**How to revisit:** if in-Unity conversion is ever wanted, invoke
`pfconvert.exe` as an external process rather than linking the importer.

## D2: LOD/visibility decisions stay native; Unity mirrors residency
The frustum + screen-space-error traversal was ported verbatim from pfview
into the C API layer (recursion → explicit stack). Unity reports what it has
uploaded (`PF_ReleaseLoadedNode(uploaded)` / `PF_UnloadNode`), and the native
side keeps the draw list = visible ∧ resident. **Why:** guarantees identical
LOD behavior to the desktop viewer and keeps the boundary chatter to a few
flat arrays per frame.

## D3: POD-only C API, single-thread contract
No STL types cross the DLL boundary; all structs are fixed-layout PODs
mirrored in `PointForgeNative.cs`. All calls from the Unity main thread; the
native worker thread never surfaces except via the log callback (which only
enqueues into a ConcurrentQueue). **Why:** ABI safety and no marshalling
surprises; matches how OctreeStore is used by pfview.

## D4: 20-byte GpuVertex passthrough, stride-20 StructuredBuffer
The native `pf::GpuVertex` (float3 + rgba + intensity/classification) is
uploaded byte-for-byte; the shader unpacks with bit ops. **Why:** zero
repacking cost, and intensity/classification are already on the GPU for
future color modes. Stride 20 is a multiple of 4, which StructuredBuffer
requires.

## D5: Coordinate mapping via the component transform, not in data
Points stay in centred PF space (Z-up RH). The `PointForgePointCloud`
transform maps to Unity space — recommended rotation (-90,0,0) +
scale (1,-1,1) gives pf(x,y,z) → unity(x,z,y) with no chirality flip
(distances and shapes stay true). The camera is transformed *into* PF space
each frame. **Why:** no per-point transform cost, native code unchanged, and
users can reposition/rescale the cloud like any GameObject.

## D6: Graphics.RenderPrimitives with per-node buffers, quads not PSIZE
One Structured GraphicsBuffer per resident node; each point expands to a
6-vertex screen-aligned quad in the vertex shader via SV_VertexID. **Why:**
requirement forbids Mesh objects; point-sprite (`PSIZE`) support is
inconsistent across D3D11/Vulkan/Metal, quad expansion is portable and gives
square points with exact pixel sizing. Opaque + ZWrite avoids all
transparency-sorting artifacts.

## D7: Static-triplet DLL build
Built with vcpkg `x64-windows-static` (existing overlay triplet pinning MSVC
14.44) → static CRT + static zstd, dependencies = KERNEL32 only. **Why:** a
single binary in `Plugins/x86_64/` with no redistributable requirements.

## D8: Editor-mode streaming via [ExecuteAlways] + SceneView camera
In edit mode the component streams against `SceneView.lastActiveSceneView`'s
camera and the PointForge window pumps `SceneView.RepaintAll()`. **Why:** the
primary workflow is inspecting datasets in the editor without entering play
mode.
