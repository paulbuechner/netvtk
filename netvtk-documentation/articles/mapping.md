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

A hint that names a parameter (`VTK_SIZEHINT(pts, npts)`) sizes what the member hands back through it: a `T*&` of numbers is a `ref T[]`, as many elements as the count holds after the call (`npts`, or the member's result for `_`), copied right after it; the array passed in isn't read. An override follows the hints of the member it overrides: `vtkHexahedron.GetFacePoints(face, ref points)` returns the count and fills `points`.

## Strings

[!code-csharp[](../samples/Mapping.cs#strings)]

## Vectors

A `std::vector<T>` is a C# array, copied:

| C++ | C# |
|---|---|
| `std::vector<T>` parameter, by value or `const&` | `T[]`, copied in |
| `std::vector<T>&`, `std::vector<T>*` parameter | `ref T[]`, copied in and back; for the pointer, null is `nullptr` |
| `std::vector<T>` return, by value, reference or pointer | `T[]`, a copy; null for `nullptr` |

Its elements are numbers (`std::vector<vtkIdType>` is `long[]`), bools, strings, structs, copies of value classes, or VTK objects, by pointer or `vtkSmartPointer`: each element's proxy holds a reference of its own. Vectors of `size_t`, of vectors, of pairs or of pointers to other classes are left out.

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

Instances over `long long` are in (`vtkIdTypeArray`'s base); those over `long`, `unsigned long`, `size_t` and `wchar_t`, whose width differs between platforms, are left out, as are the templates of VTK's third-party libraries.

The typed arrays' bulk copies `ToArray`, `CopyFrom` and `AsSpan` are members of these instances, `FromArray` a static member of each typed array: [Data and arrays](data.md#bulk-copies).

## Value classes

A class that isn't a VTK object (`vtkHardwareSelector::PixelInformation`, `vtkRenderTimerLog::Frame`) is a C# class too, and its public fields are properties:

| Field | Property |
|---|---|
| number, enum, `std::string`, struct, value class | read and written, a class as a copy; a const one read only |
| C array, `std::array` | a C# array of its length, read and written |
| `std::vector` | a C# array (see [Vectors](#vectors)), read and written; one of VTK objects by pointer read only |
| VTK object | read only: a proxy with a reference of its own; a `vtkSmartPointer` one read and written |
| `const char*`, `char*` | read only: a string |
| pointer to another class | read only: a proxy that borrows the object |
| `void*` | an `IntPtr`, read and written; any other pointer read only |

Fields of other types (maps, sets) are left out.

## Left out

What can't be mapped is left out, each member logged with its reason when NetVtk is generated: `Finalize()`, `PrintSelf` (`ToString()` gives VTK's `Print` text instead), and what VTK marks `VTK_WRAPEXCLUDE`.
