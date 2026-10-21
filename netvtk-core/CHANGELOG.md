# Changelog

Notable changes to the NetVtk packages (`NetVtk`, `NetVtk.runtime.<rid>`), in the format of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). A version is the VTK version the bindings wrap and NetVtk's revision: `9.7.1.2` is the second release for VTK 9.7.1.

Changes wait in `changes/`, a file each (`python build.py change added "..."`), until the release PR writes them here (`python build.py release`). Each release's section becomes its GitHub release notes and the packages' release notes.
## [9.7.1.1-next.2] - 2026-10-21

### Added

- Reverse Z: `vtkRenderWindow.SetUseReverseZ` gives a window 32-bit float depth from 1 at the near plane to 0 at the far one, as precise far away as near, so the near plane can come close to the eye in a large scene. It needs OpenGL 4.5 or ARB_clip_control (without, the window turns it off and warns); `DisplayToWorld` follows it. What handles depth itself follows it too: impostor spheres and tubes, the sphere and stick mappers, 2-D actors and labels, depth peeling, OIT, SSAO, EDL, pickers, widgets and the volume mappers.
- A pointer VTK hands back through a parameter its size hint names is a `ref` C# array of as many elements as the hint's count holds after the call, where it was a `ref IntPtr`: `vtkCellArray.GetCellAtId(id, ref size, ref points)`, `vtkHexahedron.GetFacePoints(face, ref points)`. A pointer parameter such a hint names is a C# array, setters' too (`vtkGraph.SetEdgePoints`).
- `std::vector` parameters and returns are C# arrays, copied both ways: numbers, bools, strings, structs, value classes and VTK objects (by pointer or `vtkSmartPointer`). A parameter C++ writes back, by non-const reference or pointer, is a `ref` array that comes back filled (`vtkUniforms.GetUniform`).
- More fields of VTK's value classes are properties: C arrays and `std::array`s (C# arrays of their length), vectors, structs, value classes and `vtkSmartPointer`s to read and write; C strings, pointers to other classes and other addresses to read. Vectors of value classes are written too.
- The public fields of VTK's value classes are properties: numbers, enums and strings to read and write, VTK objects and vectors of value classes to read. `vtkHardwareSelector_PixelInformation` tells what a pixel shows after `CaptureBuffers`, and `vtkRenderTimerLog_Frame.Events` gives a window's GPU times.
- `Vtk.VtkBuild.Current` names the VTK build NetVtk's natives carry (`9.7.1#1`: VTK's version and the port-version that counts NetVtk's patches), which the port also writes into VTK's headers (`vtkNetVtkBuild.h`). A binding on NetVtk compiles it in with NetVtkInterop.i's `%netvtk_build` and checks it with `VtkBuild.Require` when it loads.
- Windows' render windows, `vtkWin32OpenGLRenderWindow` and `vtkWin32OpenGLDXRenderWindow`. The DX window renders into a texture an application's Direct3D 11 device shares (`SetD3DDeviceContext`, `Lock`/`Unlock` around a render, `BlitToTexture`); several work in one thread, a disposed one lets go of the Direct3D device, and one on a driver without `WGL_NV_DX_interop` reports an error where it would share. Their library, NetVtkWin32, is in the Windows runtime packages only.

### Fixed

- A hardware selection (`vtkHardwareSelector`) in a multisampled window no longer re-creates the window's render framebuffer twice: a capture is over twice as fast, memory no longer grows with every capture, and a `vtkWin32OpenGLDXRenderWindow` sharing that framebuffer goes on delivering frames.
- A render window, interactor style or widget representation made before any other rendering class gets OpenGL's implementations from VTK's object factory on .NET: such a window had no GPU timer, and a `vtkBoxRepresentation`'s handles drew nothing.
- win-x86: actors show where they are. MSVC's x86 code at /O2 computed `vtkActor`'s bounds wrong (and those of volumes, image slices and assemblies), so VTK culled actors whose bounds weren't symmetric about the origin and reset cameras off the scene; VTK's x86 build is /O2 /Os now.

## [9.7.1.1-next.1] - 2026-10-13

### Added

- NetVtk: C# bindings for VTK 9.7.1, generated from VTK's headers: 25 modules (data model, execution model, core, general, geometry, modeling and source filters, legacy, XML and Exodus II readers and writers, a CGNS reader, OpenGL rendering, volume rendering, annotation, interaction), VTK's names in the namespace Vtk, natives for win-x64, win-x86, linux-x64 and osx-arm64, .NET Framework 3.5+, .NET 6+ and netstandard2.0. Typed arrays copy in bulk (FromArray, ToArray, CopyFrom, AsSpan); C# handlers and events observe VTK's events; VtkOutput takes VTK's messages; native pointers in both directions; VTK's documentation comments in IntelliSense; the SWIG interface files ship in swig/, with NetVtkInterop.i for other SWIG bindings.

[9.7.1.1-next.2]: https://github.com/paulbuechner/netvtk/releases/tag/v9.7.1.1-next.2
[9.7.1.1-next.1]: https://github.com/paulbuechner/netvtk/releases/tag/v9.7.1.1-next.1
