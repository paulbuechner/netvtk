// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

namespace Vtk;

/// <summary>
/// C# arrays at the P/Invoke layer (Types.i, Std.i): fixed-size C arrays, and bool arrays as C++
/// has them, one byte each.
/// </summary>
internal static class NativeArrays
{
  /// <summary>
  /// The array for a fixed-size C array (<c>int (&amp;)[3]</c>, <c>std::array&lt;double,
  /// 3&gt;</c>), which must hold exactly its length.
  /// </summary>
  public static T[] Checked<T>(T[] array, int length, string name) =>
    array is null ? throw new ArgumentNullException(name) :
    array.Length == length ? array :
    throw new ArgumentException($"The array needs {length} elements.", name);

  /// <summary>
  /// A bool array's bytes, which P/Invoke pins. The marshaler's bool[] conversion differs between
  /// runtimes (CLR 2 writes back four-byte BOOLs despite <c>ArraySubType = U1</c>), so C# copies
  /// into bytes, and back (<see cref="CopyBack"/>).
  /// </summary>
  public static byte[] ToBytes(bool[] values)
  {
    if (values is null)
    {
      return null;
    }

    var bytes = new byte[values.Length];
    for (var i = 0; i < values.Length; i++)
    {
      bytes[i] = values[i] ? (byte)1 : (byte)0;
    }

    return bytes;
  }

  /// <summary>
  /// <paramref name="count"/> numbers at <paramref name="source"/>, copied into a new array, or
  /// null for none: a pointer VTK returns with a size hint (VtkObjects.i), into the object.
  /// Marshal.Copy has no unsigned overloads.
  /// </summary>
  public static unsafe T[] CopyOut<T>(IntPtr source, int count) where T : unmanaged
  {
    if (source == IntPtr.Zero)
    {
      return null;
    }

    var result = new T[count];
    var from = (T*)source;
    for (var i = 0; i < count; i++)
    {
      result[i] = from[i];
    }

    return result;
  }

  public static void CopyBack(byte[] bytes, bool[] values)
  {
    if (bytes is null || values is null)
    {
      return;
    }

    for (var i = 0; i < values.Length; i++)
    {
      values[i] = bytes[i] != 0;
    }
  }
}
