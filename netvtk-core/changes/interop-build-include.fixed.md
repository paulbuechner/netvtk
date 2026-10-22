NetVtkInterop.i's `%netvtk_build` writes `#include <vtkNetVtkBuild.h>` into the binding's C++, where it wrote `%#include`, which no compiler takes.
