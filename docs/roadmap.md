# Roadmap — Pointcloud-unity

## Phase 1 — Native plugin foundation ✅
- PointForge built as `PointForgeUnity.dll` (branch `library/unity`, target `pfunity`)
- P/Invoke bindings + managed wrapper
- Open an existing PointForge dataset from Unity

## Phase 2 — Metadata & inspection ✅
- Metadata panel (points, nodes, spacing, bounds, compression)
- Bounding box gizmo + Scene View framing

## Phase 3 — Streaming & rendering ✅ (needs in-editor verification)
- Camera → native LOD traversal each frame
- Node payloads → GraphicsBuffer uploads (bounded per frame)
- URP opaque point-quad shader, vertex colors
- LRU GPU budget eviction

## Phase 4 — Controls & visibility (partially done)
- [x] Point size control (Rendering panel)
- [x] Streaming statistics (Statistics panel)
- [ ] LOD visualization (color-by-level debug mode in shader)
- [ ] Color modes: intensity / classification / elevation (data already in buffer)
- [ ] Orthographic camera support (needs native SSE variant)
- [ ] Runtime camera controller sample scene

## Phase 5 — Polish & optimization
- [ ] Buffer pooling (reuse GraphicsBuffers across evict/upload cycles)
- [ ] DepthOnly / DepthNormals shader passes (SSAO, shadows, depth prepass)
- [ ] Profiler counters (ProfilerRecorder markers for upload/render)
- [ ] Distance-attenuated / world-size point mode
- [ ] Multi-cloud support (several PointForgePointCloud instances — API already per-handle)
- [ ] In-Unity conversion trigger (invoke pfconvert.exe as external process)
