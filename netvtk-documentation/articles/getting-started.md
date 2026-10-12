# Getting started

## Install

```bash
dotnet add package NetVtk
```

One AnyCPU package for .NET Framework 3.5 and 4.5+, .NET 6, 8 and 10, and netstandard2.0; the natives come with it, in a `NetVtk.runtime.<rid>` package per platform.

> [!IMPORTANT]
> Windows needs the [Visual C++ 2015-2022 runtime](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist); rendering needs OpenGL 3.2. See [Platforms](platforms.md).

> [!TIP]
> Call `VtkOutput.Install()` at startup. Otherwise VTK shows its errors and warnings in a window of its own on Windows, on the console elsewhere. See [Observers and messages](observers.md#messages).

## A source's output

The namespace is `Vtk`:

[!code-csharp[](../samples/GettingStarted.cs#usings)]

[!code-csharp[](../samples/GettingStarted.cs#cone)]

- **`new vtkConeSource()`** calls VTK's `vtkConeSource::New()`.
- **`Update()`** runs the source; `GetOutput()` is its `vtkPolyData`.
- **`GetBounds()`** returns a `double*` VTK marks as six long: a C# `double[6]`, here -0.5 to 0.5 on each axis.
- **`using`** drops the proxy's reference at the end of the scope. See [Object lifetimes](lifetimes.md).

## A picture

Source, mapper, actor, renderer and render window, drawn off the screen and read back as pixels:

[!code-csharp[](../samples/GettingStarted.cs#picture)]

To draw into an application's window instead, see [Rendering and hosting](rendering.md).

## Where next

| To | Read |
|---|---|
| connect filters | [Pipelines](pipelines.md) |
| build a mesh with fields from C# arrays | [Data and arrays](data.md) |
| show a simulation result | [Results](results.md) |
| show VTK in WPF or Avalonia | [Rendering and hosting](rendering.md) |
| translate VTK C++ code | [From C++ to C#](mapping.md) |
