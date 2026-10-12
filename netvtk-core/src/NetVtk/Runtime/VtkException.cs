// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
#if NETFRAMEWORK
using System.Runtime.Serialization;
#endif

namespace Vtk;

/// <summary>
/// A C++ exception raised in a call into VTK (<c>std::bad_alloc</c>, <c>std::out_of_range</c>, any
/// <c>std::exception</c>), caught at the native boundary and rethrown in .NET; or what a C#
/// override VTK called back threw, as its <see cref="Exception.InnerException"/>. VTK reports its
/// own errors through <c>vtkOutputWindow</c> and error events, not exceptions.
/// </summary>
#if NETFRAMEWORK
// crosses app domains (plugin hosts, test runners) as a copy
[Serializable]
#endif
public class VtkException : Exception
{
  /// <param name="nativeType">The C++ exception's type, e.g. <c>std::bad_alloc</c>.</param>
  /// <param name="nativeMessage">Its message (<c>what()</c>).</param>
  public VtkException(string nativeType, string nativeMessage) : base(
    string.IsNullOrEmpty(nativeMessage) ? nativeType : $"{nativeType}: {nativeMessage}")
  {
    NativeType = nativeType;
    NativeMessage = nativeMessage;
  }

  /// <param name="nativeType">The C++ exception's type, e.g. <c>std::bad_alloc</c>.</param>
  /// <param name="nativeMessage">Its message (<c>what()</c>).</param>
  /// <param name="innerException">
  /// What a C# override threw, which the wrapped call reports as a
  /// <c>NetVtk_ManagedException</c>.
  /// </param>
  public VtkException(string nativeType, string nativeMessage, Exception innerException) : base(
    string.IsNullOrEmpty(nativeMessage) ? nativeType : $"{nativeType}: {nativeMessage}",
    innerException)
  {
    NativeType = nativeType;
    NativeMessage = nativeMessage;
  }

  internal static VtkException FromNative(string nativeType, string nativeMessage) =>
    nativeType == Directors.ManagedException && Directors.Take(nativeMessage) is { } thrown
      ? new VtkException(nativeType, thrown.Message, thrown)
      : new VtkException(nativeType, nativeMessage);

#if NETFRAMEWORK
  protected VtkException(SerializationInfo info, StreamingContext context) : base(info, context)
  {
    NativeType = info.GetString(nameof(NativeType));
    NativeMessage = info.GetString(nameof(NativeMessage));
  }
#endif

  /// <summary>The C++ exception's type, e.g. <c>std::bad_alloc</c>.</summary>
  public string NativeType { get; }

  /// <summary>Its message (<c>what()</c>).</summary>
  public string NativeMessage { get; }

#if NETFRAMEWORK
  public override void GetObjectData(SerializationInfo info, StreamingContext context)
  {
    base.GetObjectData(info, context);
    info.AddValue(nameof(NativeType), NativeType);
    info.AddValue(nameof(NativeMessage), NativeMessage);
  }
#endif
}
