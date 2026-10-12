# Modules

NetVtk wraps 25 of VTK's modules, one SWIG module each, in five native libraries. VTK builds what they depend on too (77 libraries on Windows), but wraps only these. Every type, per module: the [class index](../classes/index.md).

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

A sixth library, `NetVtkRuntime`, holds what the others share (exceptions from callbacks, memory streams).

The RenderingCore module registers RenderingOpenGL2, InteractionStyle, RenderingFreeType and RenderingUI with VTK's object factory when its library loads, as VTK's autoinit does in a C++ application: `new vtkRenderWindow()` is the platform's OpenGL window. RenderingVolume registers RenderingVolumeOpenGL2 so: `vtkGPUVolumeRayCastMapper.New()` is the OpenGL mapper.

Exodus, CGNS and volume rendering are features of NetVtk's VTK port (`exodus`, `cgns`, `volume`), which NetVtk builds; VTK's own copies of HDF5, NetCDF, exodusII and CGNS come with them.
