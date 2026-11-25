// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Vtk;

/// <summary>
/// C# arrays at the P/Invoke layer (Types.i, Std.i): fixed-size C arrays, bool arrays as C++ has
/// them, one byte each, and the elements of a std::vector both ways.
/// </summary>
internal static class NativeArrays
{
  // a vector's block (Std.i): the elements' count, a cell for the address of what comes back, the
  // elements
  private const int BlockHeader = 2 * sizeof(long);

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

  /// <summary>
  /// The block of a vector of numbers or structs, which C++ lays out as C# does: null is no
  /// elements.
  /// </summary>
  public static unsafe byte[] Block<T>(T[] values) where T : unmanaged
  {
    var count = values?.Length ?? 0;
    var block = NewBlock(count, count * sizeof(T));
    if (count > 0)
    {
      fixed (T* from = values)
      {
        Marshal.Copy((IntPtr)from, block, BlockHeader, count * sizeof(T));
      }
    }

    return block;
  }

  /// <summary>The block of a vector of bools, a byte each.</summary>
  public static byte[] Block(bool[] values)
  {
    var count = values?.Length ?? 0;
    var block = NewBlock(count, count);
    for (var i = 0; i < count; i++)
    {
      block[BlockHeader + i] = values[i] ? (byte)1 : (byte)0;
    }

    return block;
  }

  /// <summary>
  /// The block of a vector of strings: each one's UTF-8 byte count and bytes. A null string is
  /// empty.
  /// </summary>
  public static byte[] Block(string[] values)
  {
    var count = values?.Length ?? 0;
    var texts = new byte[count][];
    var size = 0;
    for (var i = 0; i < count; i++)
    {
      texts[i] = Encoding.UTF8.GetBytes(values[i] ?? "");
      size += sizeof(long) + texts[i].Length;
    }

    var block = NewBlock(count, size);
    var at = BlockHeader;
    foreach (var text in texts)
    {
      Write(block, at, text.Length);
      Buffer.BlockCopy(text, 0, block, at + sizeof(long), text.Length);
      at += sizeof(long) + text.Length;
    }

    return block;
  }

  /// <summary>
  /// The block of a vector of VTK objects: their native addresses, zero for a null proxy.
  /// </summary>
  public static byte[] Pointers<T>(T[] values, Func<T, HandleRef> handle) where T : class
  {
    var count = values?.Length ?? 0;
    var block = NewBlock(count, count * sizeof(long));
    for (var i = 0; i < count; i++)
    {
      Write(block, BlockHeader + i * sizeof(long), handle(values[i]).Handle.ToInt64());
    }

    return block;
  }

  /// <summary>
  /// The block of a vector of value classes: the addresses of the objects C++ copies, of which
  /// none may be null.
  /// </summary>
  public static byte[] Copies<T>(T[] values, Func<T, HandleRef> handle, string name) where T : class
  {
    if (values is not null && Array.IndexOf(values, null) >= 0)
    {
      throw new ArgumentException("The array holds a null element.", name);
    }

    return Pointers(values, handle);
  }

  /// <summary>
  /// What came back into a non-const reference's block: the vector's address, or zero when the call
  /// didn't get that far.
  /// </summary>
  public static IntPtr Result(byte[] block) =>
    block is null ? IntPtr.Zero : new IntPtr(BitConverter.ToInt64(block, sizeof(long)));

  /// <summary>
  /// A vector of numbers or structs C++ gives back (Std.i's <c>NetVtk_Vector</c>, its elements'
  /// count and address), copied; null for zero.
  /// </summary>
  public static unsafe T[] Vector<T>(IntPtr vector) where T : unmanaged
  {
    if (vector == IntPtr.Zero)
    {
      return null;
    }

    var elements = Elements(vector, out var count);
    var result = new T[count];
    for (var i = 0; i < count; i++)
    {
      result[i] = ((T*)elements)[i];
    }

    return result;
  }

  /// <summary>A vector of bools C++ gives back, a byte each; null for zero.</summary>
  public static unsafe bool[] Bools(IntPtr vector)
  {
    if (vector == IntPtr.Zero)
    {
      return null;
    }

    var elements = Elements(vector, out var count);
    var result = new bool[count];
    for (var i = 0; i < count; i++)
    {
      result[i] = ((byte*)elements)[i] != 0;
    }

    return result;
  }

  /// <summary>A vector of strings C++ gives back, decoded; null for zero.</summary>
  public static unsafe string[] Strings(IntPtr vector)
  {
    if (vector == IntPtr.Zero)
    {
      return null;
    }

    var elements = Elements(vector, out var count);
    var result = new string[count];
    for (var i = 0; i < count; i++)
    {
      result[i] = Utf8.Decode(((IntPtr*)elements)[i]);
    }

    return result;
  }

  /// <summary>
  /// A vector of objects C++ gives back, each the proxy <paramref name="proxy"/> makes of its
  /// address (null for zero), which takes what the wrapper gave it: a VTK object's reference, a
  /// value class's copy. Null for zero.
  /// </summary>
  public static unsafe T[] Proxies<T>(IntPtr vector, Func<IntPtr, T> proxy) where T : class
  {
    if (vector == IntPtr.Zero)
    {
      return null;
    }

    var elements = Elements(vector, out var count);
    var result = new T[count];
    for (var i = 0; i < count; i++)
    {
      var address = ((IntPtr*)elements)[i];
      result[i] = address == IntPtr.Zero ? null : proxy(address);
    }

    return result;
  }

  // a block with its header, the count and an empty cell, and room for the elements
  private static byte[] NewBlock(int count, int size)
  {
    var block = new byte[BlockHeader + size];
    Write(block, 0, count);
    return block;
  }

  private static void Write(byte[] block, int at, long value) =>
    Buffer.BlockCopy(BitConverter.GetBytes(value), 0, block, at, sizeof(long));

  // a NetVtk_Vector's elements: a long long count, then their address
  private static unsafe IntPtr Elements(IntPtr vector, out int count)
  {
    count = (int)*(long*)vector;
    return *(IntPtr*)((byte*)vector + sizeof(long));
  }
}
