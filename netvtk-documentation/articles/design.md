# Design decisions

Why NetVtk is built the way it is.

No maintained open-source .NET binding of VTK exists: Kitware's ActiViz is proprietary and licensed per version, its last open-source release 5.8. NetVtk is one, MIT-licensed and generated, for simulation results in .NET and as a base for other bindings ([Native interop](interop.md#a-binding-on-netvtk)).

## Decided

| Question | Decision | Why |
|---|---|---|
| VTK version | 9.7.1, through NetVtk's own vcpkg port | vcpkg's port is at 9.3.0 (2023) |
| Modules | 25 ([Modules](modules.md)): data, filters, readers, rendering, interaction; more as port features | package size, build time |
| Namespace | one, `Vtk` | VTK's class names are unique (the `vtk` prefix); a pipeline touches six modules or more |
| Names | VTK's, unchanged | C++ examples and VTK's reference carry over line by line |
| Wrapping | SWIG with NetVtk's typemaps, driven by `netvtk-gen`, a C# generator on libclang | VTK's headers say what a binding needs to know (hints, macros, module properties) |
| Frameworks | net35, net45, net452, net462, net6.0, net8.0, net10.0, netstandard2.0 | old .NET Framework applications too; the typemap library handles CLR 2 |
| Lifetime | the proxy is the object and holds one reference; `Dispose` and the finalizer drop it | VTK's reference counts decide lifetimes, as in C++ |
| Size hints | a returned pointer VTK marks with its length is a C# array of that length; so is one a member hands back through a parameter a hint names, as long as the count after the call | `GetBounds()` is six numbers, not an address; `GetCellPoints` the cell's points |
| Vectors | `std::vector` a C# array, copied both ways; a value class's fields properties, C arrays C# arrays | a C# array is what C# code holds, and a copy ties no proxy to the vector's lifetime |
| Ids | 64-bit `vtkIdType` on every platform | ids and counts are `long` everywhere, win-x86 included |
| Callbacks | `vtkCommand` and `vtkOutputWindow` subclassable in C# (SWIG directors); handlers and events on top | VTK reports through events and its output window, not exceptions |
| Exceptions in callbacks | caught at the callback, VTK goes on, the call that led to it throws a `VtkException` carrying it | a .NET exception can't unwind through VTK, which isn't written for it |
| Arrays | bulk copies through one native copy, `Span<T>` on .NET 6+ | fields hold millions of values |
| Third-party libraries | VTK's own copies, with mangled symbols; the port depends on no other port | nothing clashes with the zlib or FreeType other libraries in the process link |
| Platform classes | Windows' render windows a module of their own, in `NetVtkWin32`, which only the Windows runtime packages hold | VTK installs their headers on Windows only; everything else builds everywhere |
| Versions | `<VTK version>.<revision>`, e.g. `9.7.1.1` | a version names the VTK it wraps |
| Layout | `netvtk-core`, `-generator`, `-documentation`, `-demos` | each folder builds on its own |

## How netvtk-gen reads VTK

- **From VTK's install:** a module's headers and dependencies come from the CMake module properties VTK installs; one SWIG module per VTK module.
- **VTK's wrapping hints** (`VTK_SIZEHINT`, `VTK_NEWINSTANCE`, `VTK_WRAPEXCLUDE`) as clang annotations, which libclang reports; not through `__VTK_WRAP__`, which changes what the headers declare.
- **VTK's macros** (`vtkSetMacro`, `vtkGetMacro`, `vtkTypeMacro`) are the members they expand to, as libclang sees them.
- **Template instances** a signature uses are classes named by their arguments (`vtkAOSDataArrayTemplate_double`); the classes deriving from them derive from them in C# too.
- **Only what links:** members are checked against the export tables of VTK's DLLs.
- **Committed, deterministic output:** the generated files are committed in netvtk-core, so it builds without the generator; each skipped member is logged with its reason.

## VTK's patches

NetVtk builds Kitware's v9.7.1 with five patches (`netvtk-core/vcpkg/ports/vtk`), each with tests in netvtk-core. The members they add come last in their classes: a library compiled against VTK's unpatched headers finds every other member where it was.

- **`vtkWin32OpenGLDXRenderWindow` makes its own OpenGL context current** before its interop calls (`Lock`, `Unlock`, registering and unregistering shared textures). VTK used whichever was current, while `WGL_NV_DX_interop` registers a texture name of the current context: a window resized after another rendered registered its texture in the other's context, and the process crashed a moment later. Without an interop device (a driver lacking the extension) it reports an error where it crashed.
- **A deleted `vtkWin32OpenGLDXRenderWindow` closes its interop device:** its destructor unregisters the shared textures and closes the device while its context lives. VTK never closed it, and the device held the application's Direct3D device alive, with every texture made on it: about 100 MiB and 187 handles per window made and disposed.
- **`vtkOpenGLHardwareSelector` keeps the render framebuffer:** in a multisampled window its passes render into the window's one-sample resolve framebuffer. VTK set the window's samples to 0 and back, which re-creates the render framebuffer twice per capture: a capture took 7.3 ms instead of 2.7 (2400x1500 pixels, 4x), memory grew by 172 MiB over the first 10, and a DX window's shared texture was no longer the one it rendered into. Disabling `GL_MULTISAMPLE` instead keeps the framebuffer but names a third actor where two surfaces cross: NVIDIA's driver tests depth per sample with multisampling disabled.
- **Reverse Z,** `vtkRenderWindow::UseReverseZ` ([Rendering and hosting](rendering.md#reverse-z)). Fixed-point depth from 0 at the near plane to 1 at the far one resolves less and less toward the far plane, the more so the larger the clipping range's ratio: a viewer of a large model had to keep its near plane at a 20,000th of the far one, and clipped what the eye came close to.
- **Reverse Z where depth is handled:** impostor spheres and tubes, the sphere and stick mappers, 2-D actors and labels, the translucent passes, SSAO, EDL, depth of field, point fill, shadow maps, the skybox, pickers, widgets and the volume mappers follow `UseReverseZ`. Without it, 2-D actors fell outside the clip range and vanished, translucency peeled in the wrong order and pickers took the farthest depth for the nearest.

## Open

Open work is kept in the repository's [plan](https://github.com/paulbuechner/netvtk/tree/main/plan), a file per topic.
