// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace Vtk;

/// <summary>
/// The VTK build NetVtk's natives carry: VTK's version and the port-version of NetVtk's VTK port,
/// which counts its patches, as the port's <c>vtkNetVtkBuild.h</c> names it (<c>9.7.1#1</c>). A
/// native binding compiled against VTK's headers runs on these libraries, so it must have been
/// compiled against the same build: it checks with <see cref="Require"/> when it loads
/// (NetVtkInterop.i's <c>%netvtk_build</c>).
/// </summary>
public static class VtkBuild
{
  private static string _current;

  /// <summary>The build NetVtk's natives carry, such as <c>9.7.1#1</c>.</summary>
  public static string Current => _current ??= Utf8.Decode(NetVtk_VtkBuild());

  /// <summary>
  /// Checks that <paramref name="compiledAgainst"/>, the build the native library of
  /// <paramref name="binding"/> was compiled against, is <see cref="Current"/>.
  /// </summary>
  /// <exception cref="ArgumentNullException">
  /// <paramref name="compiledAgainst"/> is null.
  /// </exception>
  /// <exception cref="InvalidOperationException">The builds differ.</exception>
  public static void Require(string compiledAgainst, string binding)
  {
    if (compiledAgainst is null)
    {
      throw new ArgumentNullException(nameof(compiledAgainst));
    }

    if (compiledAgainst != Current)
    {
      throw new InvalidOperationException(
        $"{binding} was compiled against VTK {compiledAgainst}, NetVtk carries VTK {Current}: build it against the "
        + "VTK of this NetVtk release (its vcpkg registry, triplets and features).");
    }
  }

  [DllImport(NativeLibraryLoader.RuntimeLibrary, CallingConvention = CallingConvention.Cdecl)]
  private static extern IntPtr NetVtk_VtkBuild();
}
