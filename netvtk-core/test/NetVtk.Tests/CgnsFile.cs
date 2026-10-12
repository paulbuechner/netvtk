// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#if NET5_0_OR_GREATER
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace NetVtk.Tests;

/// <summary>
/// A CGNS file for the reader's test, written with the CGNS library VTK builds (vtkcgns, its
/// functions named vtkcgns_cg_*): VTK has no CGNS writer. One unstructured zone, hexahedra, a field
/// at the vertices. CGNS's sizes (cgsize_t) are pointer-sized in VTK's build (CG_BUILD_64BIT on
/// 64-bit targets only), its enums ints.
/// </summary>
internal static class CgnsFile
{
  private const int ModeWrite = 1, Unstructured = 3, RealDouble = 4, Hexa8 = 17, Vertex = 2;

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int Open(string file, int mode, out int fn);

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int BaseWrite(int fn, string name, int cellDim, int physDim, out int b);

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int ZoneWrite(int fn, int b, string name, nint[] size, int type, out int z);

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int CoordWrite(int fn, int b, int z, int type, string name, double[] values,
                                  out int c);

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int SectionWrite(int fn, int b, int z, string name, int type, nint start,
                                    nint end, int boundary, nint[] elements, out int s);

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int SolutionWrite(int fn, int b, int z, string name, int location, out int s);

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int FieldWrite(int fn, int b, int z, int s, int type, string name,
                                  double[] values, out int f);

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate int Close(int fn);

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  private delegate IntPtr GetError();

  /// <summary>
  /// Writes <paramref name="path"/>: the points <paramref name="xyz"/> (x, y, z after each other),
  /// the hexahedra <paramref name="hexahedra"/> (8 one-based point numbers each) and the field
  /// <paramref name="field"/> named <paramref name="fieldName"/>, a value per point.
  /// </summary>
  public static void Write(string path, double[] xyz, long[] hexahedra, string fieldName,
                           double[] field)
  {
    var library = NativeLibrary.Load(LibraryPath());

    T Function<T>(string name) where T : Delegate =>
      Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, $"vtkcgns_{name}"));

    var error = Function<GetError>("cg_get_error");

    void Check(int status)
    {
      if (status != 0)
      {
        throw new InvalidOperationException(
          $"CGNS call failed ({status}): {Marshal.PtrToStringAnsi(error())}");
      }
    }

    var points = xyz.Length / 3;
    var cells = hexahedra.Length / 8;
    Check(Function<Open>("cg_open")(path, ModeWrite, out var fn));
    Check(Function<BaseWrite>("cg_base_write")(fn, "Base", 3, 3, out var b));
    Check(Function<ZoneWrite>("cg_zone_write")(fn, b, "Zone", [points, cells, 0], Unstructured,
                                               out var z));
    string[] names = ["CoordinateX", "CoordinateY", "CoordinateZ"];
    for (var axis = 0; axis < 3; axis++)
    {
      var coordinates = Enumerable.Range(0, points).Select(p => xyz[3 * p + axis]).ToArray();
      Check(Function<CoordWrite>("cg_coord_write")(fn, b, z, RealDouble, names[axis], coordinates,
                                                   out _));
    }

    Check(Function<SectionWrite>("cg_section_write")(fn, b, z, "Hexahedra", Hexa8, 1, cells, 0,
                                                     Array.ConvertAll(hexahedra, n => (nint)n),
                                                     out _));
    Check(Function<SolutionWrite>("cg_sol_write")(fn, b, z, "Solution", Vertex, out var s));
    Check(Function<FieldWrite>("cg_field_write")(fn, b, z, s, RealDouble, fieldName, field, out _));
    Check(Function<Close>("cg_close")(fn));
  }

  // the build's vtkcgns, where the tests' natives are (x64\ and x86\ on Windows, runtimes\
  // elsewhere)
  private static string LibraryPath()
  {
    var directory = OperatingSystem.IsWindows()
      ? Path.Combine(AppContext.BaseDirectory, Environment.Is64BitProcess ? "x64" : "x86")
      : Path.Combine(AppContext.BaseDirectory, "runtimes",
                     OperatingSystem.IsMacOS() ? "osx-arm64" : "linux-x64", "native");
    return Directory.GetFiles(directory, "*vtkcgns*")
             .FirstOrDefault(f => f.EndsWith(".dll", StringComparison.Ordinal)
                                  || f.Contains(".so", StringComparison.Ordinal)
                                  || f.EndsWith(".dylib", StringComparison.Ordinal))
           ?? throw new FileNotFoundException($"no vtkcgns library in {directory}");
  }
}
#endif
