# Pipelines

Sources and readers make data, filters change it, mappers draw it: VTK's pipeline, in C# as in C++.

## Sources

[!code-csharp[](../samples/Pipelines.cs#source)]

Counts and ids are `long`: VTK's `vtkIdType`, 64 bits on every platform.

## Connecting filters

A filter's input is another algorithm's output port. Nothing runs until `Update()` (or a render) asks for output; then each algorithm runs if it or something before it changed since its last run:

[!code-csharp[](../samples/Pipelines.cs#connect)]

`GetOutput()` gives the same data object after each run, updated in place.

Data no algorithm made goes in with `SetInputData`:

[!code-csharp[](../samples/Pipelines.cs#input-data)]

## Drawing

[!code-csharp[](../samples/Pipelines.cs#scene)]

The renderer draws once a render window holds it: [Rendering and hosting](rendering.md).

## From C++

| C++ | C# |
|---|---|
| `vtkNew<vtkSphereSource> sphere;` | `using var sphere = new vtkSphereSource();` |
| `auto sphere = vtkSmartPointer<vtkSphereSource>::New();` | the same, or `vtkSphereSource.New()` |
| `sphere->SetRadius(2);` | `sphere.SetRadius(2);` |
| `filter->SetInputConnection(sphere->GetOutputPort());` | `using var port = sphere.GetOutputPort();`<br>`filter.SetInputConnection(port);` |
| `vtkPolyData* output = filter->GetOutput();` | `using var output = filter.GetOutput();` |
| `double* bounds = output->GetBounds();` | `double[] bounds = output.GetBounds();` |
| `actor->GetProperty()->SetColor(1, 0, 0);` | `using (var look = actor.GetProperty()) look.SetColor(1, 0, 0);` |
| `vtkPolyData::SafeDownCast(object)` | `vtkPolyData.SafeDownCast(object)`: null if it isn't one |

Each returned object is a new proxy holding a reference: `using` drops it at once, otherwise the finalizer does. Chained calls (`actor.GetProperty().SetColor(...)`) leave theirs to the finalizer: fine for data, not for rendering objects, see [Object lifetimes](lifetimes.md#threads).
