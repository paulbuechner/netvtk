# Introduction

NetVtk is VTK 9.7.1's C++ API in C#, generated from VTK's headers. Names don't change: `vtkPolyDataMapper` is `vtkPolyDataMapper`, in the namespace `Vtk`. VTK's [reference](../classes/index.md), its examples and forum answers apply as they are, once you know [how signatures map](mapping.md).

## VTK's parts

- **Data objects** hold geometry and fields: `vtkPolyData` (surfaces, lines, points), `vtkUnstructuredGrid` (cells of any type: a mesh), `vtkImageData` (a regular grid). A field is a `vtkDataArray` with a value or vector per point or per cell.
- **Algorithms** make and change data: sources, readers, filters, writers. Connected output to input, they form a pipeline, which runs when asked for output.
- **Rendering:** a mapper turns data into graphics, an actor places it with its look, a renderer holds actors, lights and a camera, a render window draws its renderers.
- **Objects count references.** VTK frees an object when its last reference goes; a C# proxy holds one, see [Object lifetimes](lifetimes.md).

## Classes by task

| Task | Classes | Guide |
|---|---|---|
| Points, cells, fields | `vtkPoints`, `vtkCellArray`, `vtkUnstructuredGrid`, `vtkPolyData`, `vtkDoubleArray`, `vtkPointData`, `vtkCellData` | [Data and arrays](data.md) |
| Shapes to start from | `vtkSphereSource`, `vtkConeSource`, `vtkCubeSource`, `vtkArrowSource` | [Pipelines](pipelines.md) |
| Files | `vtkXMLUnstructuredGridReader`, `vtkXMLPolyDataWriter`, `vtkUnstructuredGridReader` (legacy `.vtk`) | [Results](results.md) |
| Results | `vtkDataSetSurfaceFilter`, `vtkCutter`, `vtkContourFilter`, `vtkWarpVector`, `vtkThreshold`, `vtkGlyph3D` | [Results](results.md) |
| Colours | `vtkLookupTable`, `vtkScalarBarActor` | [Results](results.md) |
| Drawing | `vtkPolyDataMapper`, `vtkActor`, `vtkRenderer`, `vtkRenderWindow`, `vtkCamera`, `vtkWindowToImageFilter` | [Rendering and hosting](rendering.md) |
| Mouse input | `vtkGenericRenderWindowInteractor`, `vtkInteractorStyleTrackballCamera` | [Rendering and hosting](rendering.md) |
| Events, messages | `vtkCommand`, `VtkOutput` | [Observers and messages](observers.md) |

## Reading the examples

Every example is a region of a test in [netvtk-documentation/samples](https://github.com/paulbuechner/netvtk/tree/main/netvtk-documentation/samples), compiled against the NetVtk package and run. Those that need a window compile only; those that render off the screen need OpenGL. They use C# 12 (collection expressions, `using` declarations) and `using` for every VTK object; [Object lifetimes](lifetimes.md) says where that matters.
