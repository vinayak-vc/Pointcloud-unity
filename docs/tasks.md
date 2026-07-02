# Tasks — Pointcloud-unity

## Done (2026-07-02, initial integration session)
- [x] PointForge branch `library/unity`: `pfunity` DLL target + flat C API
      (`src/library/unity/PointForgeC.{h,cpp}` in the PointForge repo)
- [x] Native smoke test against `C:\UnrealProject\model\PointForgeCache_direct`
      (12.4M points): open / traverse / stream / stats verified
- [x] `Plugins/x86_64/PointForgeUnity.dll` (static CRT + zstd, KERNEL32-only)
- [x] Runtime asmdef + P/Invoke bindings + `PointForgeProject` wrapper
- [x] `PointForgePointCloud` component (streaming + RenderPrimitives rendering)
- [x] `PointForge/Points` URP shader (opaque quads, vertex color, pixel size)
- [x] `PointForgeWindow` editor window (Project / Scene View / Statistics /
      Rendering / Streaming / Console)
- [x] docs/ set created
- [x] Migrated PointForgeViewerUI to hybrid UXML/USS approach
- [x] Fixed PointForgeCameraController flight/orbit axes (gimbal lock fix)
- [x] Fixed missing `PointForgePointCloud` in test scene and applied Z-up coordinate conversion (`ApplyRecommendedTransform`)

## Next up
- [x] **Verify in the Unity editor**: open the window (PointForge > Viewer),
      open `C:\UnrealProject\model\PointForgeCache_direct`, confirm points
      render in Scene view, streaming reacts to camera, no console errors.
      (Code compiles were not run inside Unity this session.)
- [x] Migrated dataset conversion to `PointForgeConvert.dll` via P/Invoke.
- [x] Measurement tool raycasting setup
- [x] Eye-Dome Lighting (EDL) feature integration using URP RTHandle API
- [ ] LOD visualization mode (color by node level)
- [ ] Intensity / classification / elevation color modes
- [ ] GraphicsBuffer pooling
- [ ] DepthOnly pass in the shader

## Blocked / decisions needed
- Orthographic SSE support requires a native API addition (PointForge repo).
