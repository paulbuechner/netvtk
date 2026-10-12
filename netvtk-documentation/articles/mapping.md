# From C++ to C#

NetVtk is generated from VTK's headers: a C++ signature decides its C# signature. With the rules below, VTK's [reference](../classes/index.md) and C++ code translate line by line.

## Objects

[!code-csharp[](../samples/Mapping.cs#new)]

Every class deriving from `vtkObjectBase` is a C# class whose proxy holds one reference: see [Object lifetimes](lifetimes.md). A class gets a constructor when VTK gives it a `New()` and it isn't abstract. The object factory picks the class `New()` makes: `new vtkRenderWindow()` is the platform's OpenGL window.

A C# cast doesn't reach the C++ object: casts go through `SafeDownCast`.

[!code-csharp[](../samples/Mapping.cs#casts)]

`vtkSmartPointer<T>` in a signature is `T`:

[!code-csharp[](../samples/Mapping.cs#smart-pointers)]

## Names

| C++ | C# |
|---|---|
| `vtkActor` | `Vtk.vtkActor` |
| nested `vtkCommand::EventIds` | `vtkCommand_EventIds`: scopes joined by `_` |
| file-scope enum `VTKCellType` | `VTKCellType` |
| `vtkAOSDataArrayTemplate<double>` | `vtkAOSDataArrayTemplate_double`: a template instance named by its arguments |
| `Finalize()` (`vtkRenderWindow` and nine others) | left out: it clashes with C#'s finalizer |

## Size hints

VTK marks pointers it returns with their length (`VTK_SIZEHINT`): such a return is a C# array of that length, copied right after the call. Writing to the copy doesn't change the object: use the setter.

[!code-csharp[](../samples/Mapping.cs#size-hints)]

## Strings

[!code-csharp[](../samples/Mapping.cs#strings)]

## Numbers

| C++ | C# |
|---|---|
| `vtkIdType` | `long`: 64 bits on every platform |
| `vtkMTimeType` | `ulong` |
| `unsigned long` (event ids, observer tags) | `ulong`, range-checked on Windows, where `long` is 32 bits |
| `int`, `float`, `double`, `bool` | the same |
| `unsigned char` | `byte` |
| `T*` of numbers, as a parameter | `T[]` |

[!code-csharp[](../samples/Mapping.cs#width-typedefs)]

## Enums

[!code-csharp[](../samples/Mapping.cs#enums)]

## Templates

[!code-csharp[](../samples/Mapping.cs#templates)]

The typed arrays' bulk copies `ToArray`, `CopyFrom` and `AsSpan` are members of these instances, `FromArray` a static member of each typed array: [Data and arrays](data.md#bulk-copies).

## Left out

What can't be mapped is left out, each member logged with its reason when NetVtk is generated: `Finalize()`, `std::vector` parameters and returns, `PrintSelf` (`ToString()` gives VTK's `Print` text instead), and what VTK marks `VTK_WRAPEXCLUDE`.
