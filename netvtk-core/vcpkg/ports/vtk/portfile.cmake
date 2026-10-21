# netvtk's VTK: the modules NetVtk wraps and what they depend on. Third-party libraries are VTK's own copies, whose
# symbols VTK mangles (vtkzlib_, vtkfreetype_), so the port depends on no other port and nothing clashes with the zlib
# or FreeType another library in the same process links.
string(REGEX MATCH "^[0-9]+\\.[0-9]+" VTK_SHORT_VERSION "${VERSION}")

vcpkg_from_github(
    OUT_SOURCE_PATH SOURCE_PATH
    REPO Kitware/VTK
    REF "v${VERSION}"
    SHA512 749f230c9afb7479ff4f7ac606f630ee5ff725df0dfd360df153aede579737b19bb5bef2a6747c955a08c942997bb08636c1dd7282309677e9f4477616ded63a
    HEAD_REF master
    PATCHES
        0001-dx-render-window-makes-its-context-current.patch
        0002-dx-render-window-closes-its-interop-device.patch
        0003-hardware-selector-keeps-the-render-framebuffer.patch
        0004-render-window-reverse-z.patch
        0005-reverse-z-where-depth-is-handled.patch
)

# NetVtk's module set
set(NETVTK_MODULES
    CommonCore
    CommonDataModel
    CommonExecutionModel
    CommonMath
    CommonTransforms
    CommonColor
    FiltersCore
    FiltersGeneral
    FiltersGeometry
    FiltersSources
    FiltersModeling
    IOLegacy
    IOXML
    RenderingCore
    RenderingOpenGL2
    RenderingAnnotation
    RenderingFreeType
    RenderingUI
    InteractionStyle
    InteractionWidgets
)
# optional modules (the port's features): NetVtk builds them all; a build without them is another VTK
if("volume" IN_LIST FEATURES)
    list(APPEND NETVTK_MODULES RenderingVolume RenderingVolumeOpenGL2)
endif()
if("exodus" IN_LIST FEATURES)
    list(APPEND NETVTK_MODULES IOExodus)
endif()
if("cgns" IN_LIST FEATURES)
    list(APPEND NETVTK_MODULES IOCGNSReader)
endif()
set(module_options "")
foreach(module IN LISTS NETVTK_MODULES)
    list(APPEND module_options "-DVTK_MODULE_ENABLE_VTK_${module}=YES")
endforeach()

set(groups Imaging MPI Qt Rendering StandAlone Tk Views Web)
set(group_options "")
foreach(group IN LISTS groups)
    list(APPEND group_options "-DVTK_GROUP_ENABLE_${group}=DONT_WANT")
endforeach()

# 64-bit ids on every architecture: vtkIdType is a C# long on win-x86 too
vcpkg_cmake_configure(
    SOURCE_PATH "${SOURCE_PATH}"
    OPTIONS
        -DVTK_BUILD_ALL_MODULES=OFF
        ${group_options}
        ${module_options}
        -DVTK_USE_EXTERNAL=OFF
        -DVTK_USE_64BIT_IDS=ON
        -DVTK_SMP_IMPLEMENTATION_TYPE=STDThread
        -DVTK_ENABLE_WRAPPING=OFF
        -DVTK_WRAP_PYTHON=OFF
        -DVTK_WRAP_JAVA=OFF
        -DVTK_ENABLE_REMOTE_MODULES=OFF
        -DVTK_USE_MPI=OFF
        -DVTK_USE_CUDA=OFF
        -DVTK_DEBUG_LEAKS=OFF
        -DVTK_BUILD_TESTING=OFF
        -DVTK_BUILD_EXAMPLES=OFF
        -DVTK_BUILD_DOCUMENTATION=OFF
        -DVTK_VERSIONED_INSTALL=ON
        -DVTK_INSTALL_SDK=ON
    MAYBE_UNUSED_VARIABLES
        VTK_GROUP_ENABLE_Tk
        VTK_GROUP_ENABLE_Web
)

vcpkg_cmake_install()
vcpkg_copy_pdbs()

# VTK's executables (vtkProbeOpenGLVersion, vtkTestOpenGLVersion) go to tools/vtk, where the config fixup points their
# imported targets
file(GLOB vtk_tools LIST_DIRECTORIES false RELATIVE "${CURRENT_PACKAGES_DIR}/bin"
    "${CURRENT_PACKAGES_DIR}/bin/vtk*-${VTK_SHORT_VERSION}${VCPKG_TARGET_EXECUTABLE_SUFFIX}")
list(TRANSFORM vtk_tools REPLACE "${VCPKG_TARGET_EXECUTABLE_SUFFIX}$" "")
if(vtk_tools)
    vcpkg_copy_tools(TOOL_NAMES ${vtk_tools} AUTO_CLEAN)
endif()

vcpkg_cmake_config_fixup(CONFIG_PATH "lib/cmake/vtk-${VTK_SHORT_VERSION}")

# the config fixup moves the CMake files one level up (lib/cmake/vtk-X.Y -> share/vtk): their prefix is one level less
foreach(name IN ITEMS vtk-prefix VTK-vtk-module-properties)
    if(EXISTS "${CURRENT_PACKAGES_DIR}/share/vtk/${name}.cmake")
        vcpkg_replace_string("${CURRENT_PACKAGES_DIR}/share/vtk/${name}.cmake"
[[set(_vtk_module_import_prefix "${CMAKE_CURRENT_LIST_DIR}")
get_filename_component(_vtk_module_import_prefix "${_vtk_module_import_prefix}" DIRECTORY)]]
[[set(_vtk_module_import_prefix "${CMAKE_CURRENT_LIST_DIR}")]] IGNORE_UNCHANGED)
    endif()
endforeach()

file(REMOVE_RECURSE "${CURRENT_PACKAGES_DIR}/debug/include" "${CURRENT_PACKAGES_DIR}/debug/share")

# the build's name among VTK's headers, VTK's version and the port-version that counts NetVtk's patches: NetVtk reports
# it at run time (Vtk.VtkBuild), and a binding compiled against these headers compares it with NetVtk's at load
# (NetVtkInterop.i's %netvtk_build)
file(READ "${CMAKE_CURRENT_LIST_DIR}/vcpkg.json" netvtk_manifest)
string(JSON netvtk_port_version ERROR_VARIABLE netvtk_no_port_version GET "${netvtk_manifest}" "port-version")
if(netvtk_no_port_version)
    set(netvtk_port_version 0)
endif()
file(WRITE "${CURRENT_PACKAGES_DIR}/include/vtk-${VTK_SHORT_VERSION}/vtkNetVtkBuild.h" [[
// NetVtk's VTK build, written by NetVtk's vcpkg port: VTK's version and the port-version, which counts NetVtk's patches.
// A binding compiled against these headers runs on NetVtk's VTK libraries only if this matches Vtk.VtkBuild.Current.
#ifndef vtkNetVtkBuild_h
#define vtkNetVtkBuild_h
]] "#define NETVTK_VTK_BUILD \"${VERSION}#${netvtk_port_version}\"\n#endif\n")

# VTK installs its own and its third-party libraries' licenses into share/licenses
if(EXISTS "${CURRENT_PACKAGES_DIR}/share/licenses")
    file(RENAME "${CURRENT_PACKAGES_DIR}/share/licenses" "${CURRENT_PACKAGES_DIR}/share/${PORT}/licenses")
endif()
vcpkg_install_copyright(FILE_LIST "${SOURCE_PATH}/Copyright.txt" COMMENT [[
VTK's top-level Copyright.txt. The licenses of its modules and of the third-party libraries it builds are in the
licenses directory beside this file.
]])
