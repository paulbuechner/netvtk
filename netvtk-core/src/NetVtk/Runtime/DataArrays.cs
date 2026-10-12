// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace Vtk;

/// <summary>
/// The bulk copies of VTK's typed arrays (src/SWIG_files/extras/CommonCore.i): their values are one
/// block of memory, copied by NetVtkRuntime's memcpy, on every .NET.
/// </summary>
internal static class DataArrays
{
  [DllImport(NativeLibraryLoader.RuntimeLibrary, EntryPoint = "NetVtk_CopyMemory",
             CallingConvention = CallingConvention.Cdecl)]
  private static extern void CopyMemory(IntPtr target, IntPtr source, UIntPtr size);

  /// <summary>A number of values as a C# array's length.</summary>
  public static int Length(long count) =>
    count <= int.MaxValue
      ? (int)count
      : throw new InvalidOperationException(
        $"The array holds {count} values, more than a C# array can; copy it in parts.");

  /// <summary>
  /// The number of values for <paramref name="values"/>, which hold whole tuples of
  /// <paramref name="components"/>.
  /// </summary>
  public static int Values<T>(T[] values, int components) =>
    values is null
      ? throw new ArgumentNullException(nameof(values))
      : Values(values.Length, components);

  public static int Values(int length, int components) =>
    components > 0 && length % components == 0
      ? length
      : throw new ArgumentException(
        $"{length} values aren't whole tuples of {components} components.", "values");

  /// <summary>The values at <paramref name="source"/> into <paramref name="target"/>.</summary>
  public static unsafe void CopyOut<T>(IntPtr source, T[] target) where T : unmanaged
  {
    if (target.Length == 0)
    {
      return;
    }

    fixed (T* to = target)
    {
      CopyMemory((IntPtr)to, source, Bytes<T>(target.Length));
    }
  }

  /// <summary><paramref name="source"/>'s values to <paramref name="target"/>.</summary>
  public static unsafe void CopyIn<T>(T[] source, IntPtr target) where T : unmanaged
  {
    if (source.Length == 0)
    {
      return;
    }

    fixed (T* from = source)
    {
      CopyMemory(target, (IntPtr)from, Bytes<T>(source.Length));
    }
  }

  private static unsafe UIntPtr Bytes<T>(int count) where T : unmanaged =>
    new((ulong)count * (ulong)sizeof(T));
}
