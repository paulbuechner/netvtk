# NetVtk

C# bindings for the [Visualization Toolkit](https://vtk.org) (VTK) 9.7.1, generated from VTK's headers: VTK's class names, its pipelines as in C++.

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

MIT. VTK is BSD-3-Clause; the runtime packages carry its notices and those of the libraries it builds in.
