cmake_minimum_required(VERSION 3.24)

# NetVtk<Group>: the SWIG C# wrappers of a group of VTK modules (build/generated/cxx/NetVtk<Group>) linked against VTK from
# vcpkg, one library each (a Windows DLL exports at most 65535 functions). NetVtkRuntime: the hand-written src/Native.
# Driven by build.py:  python build.py native --triplet x64-windows
project(netvtk-core LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(NETVTK_GENERATED_DIR "${CMAKE_SOURCE_DIR}/build/generated/cxx" CACHE PATH "SWIG output (C++ wrappers)")
set(NETVTK_VTK_RUNTIME_DIR "" CACHE PATH "Directory with the VTK shared libraries to stage")
set(NETVTK_VTK_VERSION "" CACHE STRING "The VTK version the modules wrap (build.py passes it)")

if(NOT NETVTK_VTK_VERSION)
  message(FATAL_ERROR "NETVTK_VTK_VERSION not set; build through: python build.py native --triplet <triplet>")
endif()
find_package(VTK ${NETVTK_VTK_VERSION} EXACT CONFIG REQUIRED)
message(STATUS "VTK ${VTK_VERSION}: ${VTK_LIBRARIES}")

file(GLOB NETVTK_LIBRARY_DIRS LIST_DIRECTORIES true CONFIGURE_DEPENDS "${NETVTK_GENERATED_DIR}/NetVtk*")
if(NOT NETVTK_LIBRARY_DIRS)
  message(FATAL_ERROR "No SWIG output in ${NETVTK_GENERATED_DIR}; run: python build.py generate")
endif()

# No dot in the names: the Windows loader would treat ".Native" as the extension.
function(netvtk_library name)
  add_library(${name} SHARED ${ARGN})
  if(MSVC)
    # /bigobj: SWIG wrappers exceed the default section limit. /Zc:__cplusplus: SwigValueWrapper moves results only where
    # __cplusplus says C++11.
    target_compile_options(${name} PRIVATE /bigobj /EHsc /permissive- /W3 /Zc:__cplusplus /wd4996)
  else()
    target_compile_options(${name} PRIVATE -fvisibility=hidden -Wno-deprecated-declarations)
    if(APPLE)
      set_target_properties(${name} PROPERTIES BUILD_RPATH "@loader_path" INSTALL_RPATH "@loader_path")
    else()
      set_target_properties(${name} PROPERTIES BUILD_RPATH "$ORIGIN" INSTALL_RPATH "$ORIGIN")
      # GNU ld allows undefined symbols in a shared library; a member VTK lacks here would fail at its first call
      target_link_options(${name} PRIVATE "LINKER:--no-undefined")
    endif()
  endif()
  # Stage layout = runtimes/<rid>/native: the NetVtk libraries plus every VTK shared library.
  install(TARGETS ${name} RUNTIME DESTINATION . LIBRARY DESTINATION .)
endfunction()

# no VTK: the director failures every wrapper library shares (Directors.i) and the memory streams of Streams.i
netvtk_library(NetVtkRuntime ${CMAKE_SOURCE_DIR}/src/Native/NetVtkRuntime.cxx ${CMAKE_SOURCE_DIR}/src/Native/NetVtkStreams.cxx)
target_compile_definitions(NetVtkRuntime PRIVATE NETVTK_RUNTIME_BUILD)
target_include_directories(NetVtkRuntime PUBLIC ${CMAKE_SOURCE_DIR}/src/Native)

foreach(dir ${NETVTK_LIBRARY_DIRS})
  get_filename_component(library ${dir} NAME)
  file(GLOB sources CONFIGURE_DEPENDS "${dir}/*_wrap.cxx")
  netvtk_library(${library} ${sources})
  # the module headers netvtk-gen writes (<Module>_module.hxx, <Module>_nested.hxx)
  target_include_directories(${library} PRIVATE ${CMAKE_SOURCE_DIR}/src/SWIG_files/headers)
  target_link_libraries(${library} PRIVATE NetVtkRuntime ${VTK_LIBRARIES})
endforeach()

if(NETVTK_VTK_RUNTIME_DIR)
  if(WIN32)
    install(DIRECTORY "${NETVTK_VTK_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "*.dll")
  elseif(APPLE)
    install(DIRECTORY "${NETVTK_VTK_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "*.dylib")
  else()
    install(DIRECTORY "${NETVTK_VTK_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "*.so*")
  endif()
endif()
