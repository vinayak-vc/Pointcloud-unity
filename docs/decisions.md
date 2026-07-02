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

## D9: UI Toolkit Hybrid Approach (UXML + USS)
The `PointForgeViewerUI` now utilizes a hybrid UI Toolkit approach, defining
its layout and styles in `PointForgeViewer.uxml` and `PointForgeViewer.uss`
rather than pure C# generation. **Why:** While pure C# was optimal for AI
agents and avoided missing asset references, a UXML/USS approach allows humans
to visually edit the interface layout using Unity's UI Builder inside the IDE.
Logic is still completely contained within `PointForgeViewerUI.cs` via querying
the visual tree asset.

## D10: Camera Flight Axes and Gimbal Lock
The flight controls in `PointForgeCameraController` explicitly deconstruct the `transform.forward` vector using `Mathf.Atan2` for yaw and `Mathf.Asin` for pitch rather than extracting Unity's `eulerAngles`. **Why:** Extracting euler angles directly can lead to unexpected 180-degree flips and axis swapping when the pitch approaches +/- 90 degrees (gimbal lock). Rigorous spherical decomposition ensures that looking around the poles acts linearly.

## D11: Point Cloud Instantiation and Transform Handling
When automatically instantiating or recovering the `PointForgePointCloud` component in the scene via scripts, `ApplyRecommendedTransform()` must be called immediately. **Why:** The native PointForge C++ library outputs vertex data in Z-up right-handed coordinates. If the Unity GameObject sits at identity `(0,0,0)`, the point cloud will render upside-down/mirrored, causing the camera flight axes (which use local forward/up relative to the world) to be completely inverted. `ApplyRecommendedTransform` correctly configures the game object as `-90` around X with `(1, -1, 1)` scaling to map the coordinate space seamlessly into Unity's Y-up left-handed system without per-vertex overhead.

## D12: Convert Integration (DLL instead of Process)
- **Decision**: Replaced System.Diagnostics.Process invocation of pfconvert.exe with a native DLL library (PointForgeConvert.dll) in the Unity integration.
- **Why**: System.Diagnostics.Process is not supported and usually stripped in IL2CPP builds. Providing a DLL ensures cross-platform and IL2CPP compatibility within Unity, keeping the conversion logic directly within the Unity process space.
- **Consequence**: Implemented a new C-API pfconvert_api.cpp in the native repo and a pfconvert_dll CMake target that exposes PF_ConvertDataset and PF_Convert_SetLogCallback. Logs are sent via a callback to C#, which avoids freezing the editor and correctly feeds the Unity UI console using Task.Run.

## D13: EDL Feature (URP RTHandle API)
- **Decision**: Used RTHandle and Blitter for the Eye-Dome Lighting (EDL) ScriptableRendererFeature.
- **Why**: Unity 2022+ deprecated RenderTargetHandle and cmd.Blit(). We used RenderingUtils.ReAllocateIfNeeded and Blitter.BlitCameraTexture to stay compatible with the newest URP API guidelines.
- **Consequence**: The feature is attached to the URP asset and found at runtime using Resources.FindObjectsOfTypeAll<PointForgeEDLFeature>() because Unity 2022+ made the scriptableRendererData property internal/obsolete.
