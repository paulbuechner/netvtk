On macOS, a render timer's `FrameReady()` before its window's first render no longer crashes the process: VTK asked OpenGL for its vendor without a context (the VTK port's patch 0006).
