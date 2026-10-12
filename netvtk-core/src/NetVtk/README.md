# NetVtk

C# bindings for the [Visualization Toolkit](https://vtk.org) (VTK) 9.7.1, generated from VTK's headers: VTK's class names, its pipelines as in C++, and IntelliSense shows VTK's documentation.

```bash
dotnet add package NetVtk
```

One AnyCPU package for .NET Framework 3.5 and 4.5+, .NET 6, 8 and 10, and netstandard2.0, with the natives for Windows (x64, x86), Linux (x64) and macOS (arm64).

```csharp
using Vtk;

using var cone = new vtkConeSource();
cone.SetResolution(32);
cone.Update();
using var output = cone.GetOutput();
double[] bounds = output.GetBounds();
```

- **Objects:** `new vtkConeSource()` calls VTK's `New()`. A proxy holds one reference to its object; `Dispose` (or the garbage collector) releases it. Rendering objects release OpenGL resources when they go: dispose them on the thread that renders, or set `VtkFinalizer.Context` to its `SynchronizationContext`.
- **Arrays:** `vtkDoubleArray.FromArray(values, 3)`, `ToArray()`, `CopyFrom(values)`; on .NET 6+ `AsSpan()` is the array's memory, no copy. `ToDoubleArray()` converts any `vtkDataArray`.
- **Observers:** `AddObserver(vtkCommand_EventIds.ModifiedEvent, (sender, e) => ...)`, `Observe(...)` until disposed, or the events `ModifiedEvent`, `ErrorEvent`, `WarningEvent` and an algorithm's `StartEvent`, `ProgressEvent`, `EndEvent`. A subclass of `vtkCommand` works too. An exception a handler throws comes out of the call that triggered the event, as the `InnerException` of a `VtkException`.
- **Messages:** `VtkOutput.Message += (_, e) => ...` receives VTK's errors and warnings, which VTK would otherwise show in a window of its own (Windows) or on the console.
- **Native interop:** `NativePointer` is the `vtkObjectBase*`; `vtkObjectBase.FromNativePointer<T>(pointer)` makes a proxy for one. The SWIG interface files are in the package's `swig/` folder.

## Documentation

- [Guide](https://paulbuechner.github.io/netvtk/articles/introduction.html): pipelines, data, results, rendering and hosting, observers; every example compiles against the package in CI.
- [From C++ to C#](https://paulbuechner.github.io/netvtk/articles/mapping.html): how VTK's signatures look in C#, so VTK's own documentation and examples apply.
- [Class index](https://paulbuechner.github.io/netvtk/classes/index.html): every type, linked to VTK's reference.
- [Source and demos](https://github.com/paulbuechner/netvtk), [changes](https://github.com/paulbuechner/netvtk/blob/main/netvtk-core/CHANGELOG.md).

## License

NetVtk is MIT licensed. The IntelliSense documentation holds the doc comments of VTK's headers, and the runtime packages hold VTK's libraries and those it builds in: VTK is BSD-3-Clause. `NOTICE`, `VTK-LICENSE.txt` and each runtime package's `THIRD-PARTY-NOTICES.txt` have the details.
