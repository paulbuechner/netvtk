# Modules

NetVtk wraps 25 of VTK's modules, one SWIG module each, in five native libraries; RenderingOpenGL2's Windows-only classes are a SWIG module of their own, in a library for Windows. VTK builds what they depend on too (77 libraries on Windows), but wraps only these. Every type, per module: the [class index](../classes/index.md).

| Library | Module | For, e.g. |
|---|---|---|
| `NetVtkCommon` | CommonCore | objects, events, data arrays, lookup tables: `vtkObject`, `vtkCommand`, `vtkDoubleArray`, `vtkLookupTable`, `vtkMath` |
| | CommonMath | matrices, interpolation: `vtkMatrix4x4`, `vtkQuaternionInterpolator` |
| | CommonTransforms | `vtkTransform` |
| | CommonColor | `vtkNamedColors`, `vtkColorSeries` |
| | CommonDataModel | data sets, cells, implicit functions, locators: `vtkPolyData`, `vtkUnstructuredGrid`, `vtkImageData`, `vtkPlane`, `vtkCellLocator` |
| | CommonExecutionModel | the pipeline: `vtkAlgorithm`, `vtkStreamingDemandDrivenPipeline` |
| `NetVtkFilters` | FiltersCore | `vtkContourFilter`, `vtkCutter`, `vtkElevationFilter`, `vtkThreshold`, `vtkGlyph3D`, `vtkProbeFilter`, `vtkPolyDataNormals`, `vtkCellDataToPointData`, `vtkArrayCalculator` |
| | FiltersGeneral | `vtkWarpVector`, `vtkWarpScalar`, `vtkClipDataSet`, `vtkShrinkFilter`, `vtkTransformFilter` |
| | FiltersGeometry | `vtkDataSetSurfaceFilter`, `vtkGeometryFilter` |
| | FiltersSources | `vtkSphereSource`, `vtkConeSource`, `vtkCubeSource`, `vtkPlaneSource`, `vtkArrowSource` |
| | FiltersModeling | `vtkOutlineFilter`, `vtkLinearExtrusionFilter`, `vtkRibbonFilter` |
| `NetVtkIO` | IOCore | the readers' and writers' bases: `vtkWriter` (`SetInputData`, `Write`), `vtkDelimitedTextWriter` |
| | IOLegacy | legacy `.vtk` files: `vtkUnstructuredGridReader`, `vtkPolyDataWriter` |
| | IOXML | VTK's XML files (`.vtu`, `.vtp`, `.vti`, `.vts`, ...): `vtkXMLUnstructuredGridReader`, `vtkXMLPolyDataWriter` |
| | IOExodus | Exodus II files (`.exo`, `.e`): `vtkExodusIIReader`, `vtkExodusIIWriter` |
| | IOCGNSReader | CGNS files (`.cgns`): `vtkCGNSReader` |
| `NetVtkRendering` | RenderingCore | `vtkRenderWindow`, `vtkRenderer`, `vtkActor`, `vtkPolyDataMapper`, `vtkCamera`, `vtkLight`, `vtkTextActor`, `vtkWindowToImageFilter` |
| | RenderingOpenGL2 | the OpenGL classes the object factory makes: `vtkOpenGLRenderer`, `vtkOpenGLPolyDataMapper` |
| | RenderingAnnotation | `vtkScalarBarActor`, `vtkCubeAxesActor`, `vtkAxesActor`, `vtkCaptionActor2D` |
| | RenderingFreeType | text: `vtkFreeTypeTools` |
| | RenderingUI | `vtkGenericRenderWindowInteractor` |
| | RenderingVolume | volume mappers: `vtkGPUVolumeRayCastMapper`, `vtkFixedPointVolumeRayCastMapper` (`vtkVolume` and `vtkVolumeProperty` are RenderingCore's) |
| | RenderingVolumeOpenGL2 | `vtkSmartVolumeMapper`, `vtkOpenGLGPUVolumeRayCastMapper` |
| `NetVtkInteraction` | InteractionStyle | `vtkInteractorStyleTrackballCamera`, `vtkInteractorStyleJoystickCamera` |
| | InteractionWidgets | `vtkImplicitPlaneWidget2`, `vtkBoxWidget2`, `vtkSliderWidget`, `vtkScalarBarWidget`, `vtkOrientationMarkerWidget` |
| `NetVtkWin32` | RenderingOpenGL2Win32 | RenderingOpenGL2's Windows render windows: `vtkWin32OpenGLRenderWindow` (what `new vtkRenderWindow()` makes there), `vtkWin32OpenGLDXRenderWindow` (renders into a texture Direct3D 11 shares) |

`NetVtkRuntime` holds what the others share (exceptions from callbacks, memory streams). `NetVtkWin32` is in the Windows runtime packages only: elsewhere the first call to its classes throws a `TypeInitializationException`, a `DllNotFoundException` inside.

The RenderingCore module registers RenderingOpenGL2, InteractionStyle, RenderingFreeType and RenderingUI with VTK's object factory when its library loads, as VTK's autoinit does in a C++ application: `new vtkRenderWindow()` is the platform's OpenGL window. `NetVtkWin32` and `NetVtkInteraction` register them too, since their classes make RenderingCore's objects in their constructors (a window's render timer, a widget representation's actors and mappers): whichever library a process calls first, they are OpenGL's. RenderingVolume registers RenderingVolumeOpenGL2 so: `vtkGPUVolumeRayCastMapper.New()` is the OpenGL mapper.

Exodus, CGNS and volume rendering are features of NetVtk's VTK port (`exodus`, `cgns`, `volume`), which NetVtk builds; VTK's own copies of HDF5, NetCDF, exodusII and CGNS come with them.
