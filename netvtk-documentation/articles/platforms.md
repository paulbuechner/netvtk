# Platforms

## Frameworks

One AnyCPU assembly per framework: .NET Framework 3.5, 4.5, 4.5.2 and 4.6.2, .NET 6, 8 and 10, and netstandard2.0. `AsSpan()` needs .NET 6 or later.

## Natives

`NetVtk` depends on one package of natives per platform, `NetVtk.runtime.<rid>`: VTK's libraries and NetVtk's six (`NetVtkCommon`, `NetVtkFilters`, `NetVtkIO`, `NetVtkRendering`, `NetVtkInteraction`, `NetVtkRuntime`); Windows' have a seventh, `NetVtkWin32`, with the Windows render windows (`vtkWin32OpenGLRenderWindow`, `vtkWin32OpenGLDXRenderWindow`), whose calls throw elsewhere.

| RID | Needs | Tested |
|---|---|---|
| win-x64, win-x86 | the [Visual C++ 2015-2022 runtime](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) | every framework, net35 on CLR 2 |
| linux-x64 | glibc 2.35 or newer; X11, GL and EGL libraries | .NET 10, 8 and 6, in Docker (Ubuntu 24.04) |
| osx-arm64 | macOS 14 or newer | .NET 10, 8 and 6 on CI's macOS runners, without the rendering tests (no display) |

.NET apps load the natives from `runtimes/<rid>/native`. .NET Framework apps get them in `x64\` and `x86\` next to the app, so AnyCPU works; NetVtk picks the folder for the process.

`vtkIdType` is 64 bits on every platform, win-x86 too: ids and counts are `long` everywhere.

win-x86's VTK is built at `/O2 /Os` (netvtk-core's x86 triplet): MSVC's x86 code at `/O2` alone computed `vtkActor`'s bounds wrong, so actors were culled and cameras reset off the scene.

## Rendering

Rendering needs OpenGL 3.2; [reverse Z](rendering.md#reverse-z) 4.5 or `ARB_clip_control`.

- **Windows:** the GPU's driver. `vtkWin32OpenGLDXRenderWindow` renders into a texture an application's Direct3D 11 device shares, with the driver's `WGL_NV_DX_interop`: see [Direct3D](rendering.md#direct3d).
- **Linux:** the natives are built against X11, GL and EGL (netvtk-core's Dockerfile installs `libgl1-mesa-dev`, `libegl-dev`, `libx11-dev`, `libxext-dev`, `libxi-dev`, `libxt-dev`, `libxcursor-dev`). Without a display, run under Xvfb: the tests render there with Mesa's llvmpipe.
- **macOS:** VTK's Cocoa render window calls AppKit, which wants the main thread, also off the screen: render there.
- **No software fallback:** NetVtk renders with the system's OpenGL driver and ships none of its own. CI runs the rendering tests on Linux only, under Xvfb with Mesa's llvmpipe: its Windows and macOS runners have no GPU driver.
- **In an application's window:** a Win32 child window on Windows, an X11 one on Linux, an NSView on macOS. See [Rendering and hosting](rendering.md#in-an-applications-window).

## Package sizes

| Package | Size |
|---|---|
| `NetVtk`: eight assemblies, one per framework, with VTK's documentation for IntelliSense, and the SWIG files | 31.7 MB |
| `NetVtk.runtime.win-x64`: 77 VTK libraries, NetVtk's seven, the notices | 29.5 MB |
| `NetVtk.runtime.win-x86` | 30.0 MB |
| `NetVtk.runtime.linux-x64` | 76.7 MB |

Measured on a local pack of 9.7.1.1; nuget.org takes packages up to 250 MB.
