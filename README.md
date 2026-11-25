<div align="center">
  <h1>NetVtk</h1>
</div>

<div align="center">
  <p>The Visualization Toolkit (VTK) 9.7.1 for .NET: C# bindings generated from VTK's headers</p>
</div>

<div align="center">

<a href="https://github.com/paulbuechner/netvtk/actions/workflows/build.yml">
<img alt="Build" src="https://img.shields.io/github/actions/workflow/status/paulbuechner/netvtk/build.yml?branch=main&label=build&style=for-the-badge">
</a>
<a href="https://www.nuget.org/packages/NetVtk">
<img alt="NuGet" src="https://img.shields.io/nuget/vpre/NetVtk?logo=nuget&label=nuget&style=for-the-badge">
</a>
<a href="https://paulbuechner.github.io/netvtk/">
<img alt="Documentation" src="https://img.shields.io/github/actions/workflow/status/paulbuechner/netvtk/docs.yml?branch=main&label=docs&style=for-the-badge">
</a>
<a href="https://vtk.org">
<img alt="VTK" src="https://img.shields.io/badge/VTK-9.7.1-blue?style=for-the-badge">
</a>
<a href="LICENSE">
<img alt="License" src="https://img.shields.io/github/license/paulbuechner/netvtk?style=for-the-badge">
</a>

</div>

<br/>

[The Visualization Toolkit](https://vtk.org) (VTK) for .NET, for simulation results: fields on meshes, isosurfaces, cuts, warps, scalar bars. VTK's class and method names unchanged, in one namespace, `Vtk`, and its pipelines as in C++, so VTK's reference and C++ examples apply as they are. IntelliSense shows VTK's documentation.

## Install

```bash
dotnet add package NetVtk
```

One AnyCPU package for .NET Framework 3.5 and 4.5+, .NET 6, 8 and 10, and netstandard2.0, with the natives of every platform (`NetVtk.runtime.<rid>`, which it depends on):

| Platform | Needs |
|---|---|
| Windows x64, x86 | the Visual C++ 2015-2022 runtime |
| Linux x64 | glibc 2.35 or newer; X11, GL and EGL libraries |
| macOS arm64 | macOS 14 or newer |

## Example

A sphere's height as a field on its surface, cut into nine rings, written as a `.vtp` file:

```csharp
using System;
using Vtk;

namespace Contours;

// a sphere's height as a field on its surface, cut into rings of equal height, written as .vtp
internal static class Program
{
  private static void Main()
  {
    using var sphere = new vtkSphereSource();
    sphere.SetThetaResolution(64);
    sphere.SetPhiResolution(32);

    // each point's height as the field "Elevation": 0 at the bottom, 1 at the top
    using var elevation = new vtkElevationFilter();
    elevation.SetLowPoint(0, 0, -0.5);
    elevation.SetHighPoint(0, 0, 0.5);
    using var spherePort = sphere.GetOutputPort();
    elevation.SetInputConnection(spherePort);

    // nine rings, where the field is 0.1, 0.2, ..., 0.9
    using var contours = new vtkContourFilter();
    using var elevationPort = elevation.GetOutputPort();
    contours.SetInputConnection(elevationPort);
    contours.GenerateValues(9, 0.1, 0.9);

    // Update runs the pipeline up to the contours
    contours.Update();
    using var rings = contours.GetOutput();
    Console.WriteLine($"{rings.GetNumberOfPoints()} points, {rings.GetNumberOfLines()} segments");

    using var writer = new vtkXMLPolyDataWriter();
    writer.SetFileName("rings.vtp");
    writer.SetInputData(rings);
    writer.Write();
  }
}
```

## What's covered

25 of VTK's modules, about 2,450 types:

- data sets and arrays, the pipeline, VTK's core;
- general, geometry, modeling and source filters;
- VTK's XML and legacy files, Exodus II and CGNS;
- OpenGL rendering with annotations, text and volumes; interaction styles and widgets;
- 3D views in a WPF or Avalonia window on Windows, an X11 window on Linux, an NSView on macOS;
- arrays copied in bulk (`FromArray`, `ToArray`, `AsSpan`), C# handlers for VTK's events, VTK's messages as a .NET event;
- other SWIG bindings building on NetVtk's proxies (`swig/interop/NetVtkInterop.i` in the package).

## Documentation

[paulbuechner.github.io/netvtk](https://paulbuechner.github.io/netvtk/):

- [Guide](https://paulbuechner.github.io/netvtk/articles/introduction.html): pipelines, data and arrays, results, rendering and hosting, observers; every example compiles against the package in CI, and all but those that need a window run there.
- [From C++ to C#](https://paulbuechner.github.io/netvtk/articles/mapping.html): how VTK's signatures look in C#.
- [Class index](https://paulbuechner.github.io/netvtk/classes/index.html): every type, linked to VTK's reference.

## Repository

| Folder | Content |
|---|---|
| [netvtk-core](netvtk-core) | VTK's vcpkg port, typemap library, generated SWIG interfaces, the `NetVtk` assembly, tests, NuGet packages |
| [netvtk-generator](netvtk-generator) | `netvtk-gen`: reads VTK's headers and writes netvtk-core's interface files |
| [netvtk-documentation](netvtk-documentation) | the documentation site and the examples it shows |
| [netvtk-demos](netvtk-demos) | WPF and Avalonia viewers: a `.vtu` coloured by a field, with a scalar bar, a cut plane, contours and a warp |
| [versions](versions) | the vcpkg registry of VTK's port, for projects that build the same VTK |

Building from source: [netvtk-core/README.md](netvtk-core/README.md), or the [building guide](https://paulbuechner.github.io/netvtk/articles/building.html).

## Versions

A version is the VTK version the bindings are generated for and NetVtk's revision: `9.7.1.2` is the second release for VTK 9.7.1. A release's tag (`v9.7.1.1`) is the commit to pin the vcpkg registry at. Changes per release: [netvtk-core/CHANGELOG.md](netvtk-core/CHANGELOG.md).

## License

MIT, see [LICENSE](LICENSE). VTK is BSD-3-Clause, and so is the IntelliSense documentation taken from its headers; the runtime packages carry its notices and those of the libraries it builds in, see [netvtk-core/NOTICE](netvtk-core/NOTICE). NetVtk is not affiliated with, endorsed by, or sponsored by Kitware, Inc.
