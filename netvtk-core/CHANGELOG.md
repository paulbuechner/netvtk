# Changelog

Notable changes to the NetVtk packages (`NetVtk`, `NetVtk.runtime.<rid>`), in the format of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). A version is the VTK version the bindings wrap and NetVtk's revision: `9.7.1.2` is the second release for VTK 9.7.1.

Changes wait in `changes/`, a file each (`python build.py change added "..."`), until the release PR writes them here (`python build.py release`). Each release's section becomes its GitHub release notes and the packages' release notes.
## [9.7.1.1] - 2026-10-12

### Added

- NetVtk: C# bindings for VTK 9.7.1, generated from VTK's headers: 25 modules (data model, execution model, core, general, geometry, modeling and source filters, legacy, XML and Exodus II readers and writers, a CGNS reader, OpenGL rendering, volume rendering, annotation, interaction), VTK's names in the namespace Vtk, natives for win-x64, win-x86, linux-x64 and osx-arm64, .NET Framework 3.5+, .NET 6+ and netstandard2.0. Typed arrays copy in bulk (FromArray, ToArray, CopyFrom, AsSpan); C# handlers and events observe VTK's events; VtkOutput takes VTK's messages; native pointers in both directions; the SWIG interface files ship in swig/, with NetVtkInterop.i for other SWIG bindings.

[9.7.1.1]: https://github.com/paulbuechner/netvtk/releases/tag/v9.7.1.1
