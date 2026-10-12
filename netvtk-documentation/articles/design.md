# Design decisions

Why NetVtk is built the way it is.

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
| Size hints | a returned pointer VTK marks with its length is a C# array of that length | `GetBounds()` is six numbers, not an address |
| Ids | 64-bit `vtkIdType` on every platform | ids and counts are `long` everywhere, win-x86 included |
| Callbacks | `vtkCommand` and `vtkOutputWindow` subclassable in C# (SWIG directors); handlers and events on top | VTK reports through events and its output window, not exceptions |
| Exceptions in callbacks | caught at the callback, VTK goes on, the call that led to it throws a `VtkException` carrying it | a .NET exception can't unwind through VTK, which isn't written for it |
| Arrays | bulk copies through one native copy, `Span<T>` on .NET 6+ | fields hold millions of values |
| Third-party libraries | VTK's own copies, with mangled symbols; the port depends on no other port | nothing clashes with the zlib or FreeType other libraries in the process link |
| Versions | `<VTK version>.<revision>`, e.g. `9.7.1.1` | a version names the VTK it wraps |
| Layout | `netvtk-core`, `-generator`, `-documentation`, `-demos` | each folder builds on its own |

## How netvtk-gen reads VTK

- **From VTK's install:** a module's headers and dependencies come from the CMake module properties VTK installs; one SWIG module per VTK module.
- **VTK's wrapping hints** (`VTK_SIZEHINT`, `VTK_NEWINSTANCE`, `VTK_WRAPEXCLUDE`) as clang annotations, which libclang reports.
- **Template instances** a signature uses are classes named by their arguments (`vtkAOSDataArrayTemplate_double`); the classes deriving from them derive from them in C# too.
- **Only what links:** members are checked against the export tables of VTK's DLLs.
- **Committed, deterministic output:** the generated files are committed in netvtk-core, so it builds without the generator; each skipped member is logged with its reason.

## Open

- Hosting on macOS (an NSView): nothing here could run it yet.
- `std::vector` parameters and returns.
- More modules and readers, as port features.
