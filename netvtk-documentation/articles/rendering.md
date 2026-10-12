# Rendering and hosting

VTK draws with OpenGL, into a window of its own, a window the application gives it, or an offscreen buffer.

## Off the screen

[!code-csharp[](../samples/Rendering.cs#offscreen)]

The image's scalars are the pixels: `vtkUnsignedCharArray.ToArray()` copies them, as in [Getting started](getting-started.md#a-picture). Rendering needs OpenGL 3.2: a GPU's driver, or Mesa's llvmpipe (on Linux, under Xvfb).

## Volumes

An image's values drawn as a volume, through transfer functions for opacity and colour:

[!code-csharp[](../samples/Rendering.cs#volume)]

`vtkGPUVolumeRayCastMapper.New()` and `vtkFixedPointVolumeRayCastMapper` pick one way to draw; `vtkSmartVolumeMapper` picks for the GPU at hand. Unstructured grids go through `vtkUnstructuredGridVolumeRayCastMapper` or `vtkProjectedTetrahedraMapper`.

## In an application's window

The application makes a child window (an HWND on Windows); VTK's render window draws into it, and VTK's interactor gets the window's size, paint and mouse messages from C#:

[!code-csharp[](../samples/Rendering.cs#host)]

- **WPF:** an `HwndHost` creates the child window in `BuildWindowCore`.
- **Avalonia:** a `NativeControlHost` creates it in `CreateNativeControlCore`, after the window shows.
- **The window class** has `CS_OWNDC`: OpenGL needs its own device context. Its procedure calls the view; VTK's own window procedure stays out, so an exception a C# observer throws comes back to C#.

The [demos](https://github.com/paulbuechner/netvtk/tree/main/netvtk-demos) do all of this in a WPF and an Avalonia 12 viewer that open `.vtu` results; `NetVtk.Viewer` holds the shared part (`VtkViewer`, `ViewerWindow`).

**Linux (X11):** Avalonia's `NativeControlHost` gives an X11 window id. On a display connection the application opens and hands to VTK (`SetDisplayId`), VTK creates the child window there itself (`SetParentId`, `Initialize`, then `GetGenericWindowId()`), with the visual its OpenGL context needs; the application selects the window's mouse and structure events on that connection and passes them on to VTK's interactor, polling from a UI timer. The demos' `X11ViewerWindow` does this; its test draws into a host's X11 window under Xvfb. macOS (an NSView child) isn't done yet.

## The rendering thread

Render windows, renderers, mappers, actors and textures release OpenGL resources when they go, which needs the thread that renders: create, use and dispose them there, the render window last. For what the garbage collector finalizes, set `VtkFinalizer.Context` on that thread, as `InitializeOnUiThread` above does: the finalizer then posts the releases to it. See [Object lifetimes](lifetimes.md#threads).
