# Project Overview — Pointcloud-unity

Unity frontend for the **PointForge** out-of-core point cloud engine
(`C:\UnrealProject\PointForge`, branch `library/unity`). Unity is UI and
renderer only; all import, octree indexing, LOD selection and disk streaming
stay in the existing C++ engine, consumed through a native DLL.

- Engine repo: PointForge (C++17). Do not reimplement engine features here.
- This repo: native DLL binary, C# wrapper, streaming/rendering component,
  URP point shader, PointForge editor window.
- Datasets: directories produced by `pfconvert` (`meta.bin`, `hierarchy.bin`,
  `octree.bin`). Conversion is NOT done inside Unity — run pfconvert, then
  open the output directory via the PointForge editor window.

## Layout
```
Plugins/x86_64/PointForgeUnity.dll   native engine (built from PointForge branch library/unity)
Runtime/Native/PointForgeNative.cs   P/Invoke bindings (POD mirrors of PointForgeC.h)
Runtime/PointForgeProject.cs         managed handle wrapper + native log pump
Runtime/PointForgePointCloud.cs      streaming manager + GPU renderer component
Runtime/Shaders/PointForgePoints.shader  URP opaque point-quad shader
Editor/PointForgeWindow.cs           "PointForge" menu window (6 panels)
docs/                                this documentation set
```

## Requirements
- Unity 6000.3.x, URP 17.x
- Windows x86_64 (DLL is win64; static CRT, no extra dependencies)
- Compute-capable GPU (StructuredBuffer in vertex stage, shader target 4.5)
