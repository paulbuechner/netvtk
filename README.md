<div align="center">
  <h1>NetVtk</h1>
</div>

<div align="center">
  <p>The Visualization Toolkit (VTK) for .NET: C# bindings generated from VTK's headers</p>
</div>

<br/>

[VTK](https://vtk.org) 9.7.1 for .NET: VTK's class names unchanged, its pipelines as in C++, natives for win-x64, win-x86, linux-x64 and osx-arm64. For simulation results: fields on meshes, isosurfaces, cuts, warps, scalar bars.

```bash
dotnet add package NetVtk
```

```csharp
using Vtk;

using var reader = new vtkXMLUnstructuredGridReader();
reader.SetFileName("result.vtu");
reader.Update();
using var grid = reader.GetOutput();
using var points = grid.GetPointData();
using var stress = points.GetArray("stress");
double[] values = stress.ToDoubleArray();
```

- One AnyCPU package for .NET Framework 3.5+, .NET 6+ and netstandard2.0; the natives come in `NetVtk.runtime.<rid>`.
- Objects count references: `Dispose` releases a proxy's, the garbage collector the rest.
- Typed arrays copy in bulk (`FromArray`, `ToArray`, `AsSpan`), handlers observe VTK's events, `VtkOutput` takes its messages.
- WPF and Avalonia viewers in [netvtk-demos](netvtk-demos): a `.vtu` coloured by a field, with a scalar bar, a cut plane, contours and a warp.

Other SWIG bindings can build on NetVtk's proxies: the package ships `swig/interop/NetVtkInterop.i` for them.

## Layout

| Folder | Content |
|---|---|
| `netvtk-core/` | VTK's vcpkg port, typemap library, generated SWIG modules, the `NetVtk` assembly, tests, NuGet packs (`build.py`) |
| `netvtk-generator/` | `netvtk-gen`: reads VTK's install, writes the SWIG modules |
| `netvtk-documentation/` | documentation site |
| `netvtk-demos/` | WPF and Avalonia viewers on the packed NetVtk |
| `versions/` | the vcpkg registry of VTK's port, for projects that build the same VTK |

## License

MIT ([LICENSE](LICENSE)). VTK is BSD-3-Clause; the runtime packages carry its notices and those of the libraries it builds in.
