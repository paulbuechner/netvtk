// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace NetVtk.Viewer;

// the Objective-C runtime calls CocoaViewerView needs (libobjc, source-generated): a message is
// objc_msgSend with the method's own signature, one entry point per signature; on osx-arm64 the
// geometry structs (doubles only) go in and come back in floating-point registers, no _stret
internal static partial class CocoaMethods
{
  private const string ObjC = "/usr/lib/libobjc.A.dylib";

  // NSEventModifierFlags
  public const nuint ShiftKey = 1 << 17, ControlKey = 1 << 18, CommandKey = 1 << 20;

  // NSTrackingAreaOptions
  public const nuint TrackMouseMoved = 0x02, TrackInKeyWindow = 0x20, TrackInVisibleRect = 0x200;

  [StructLayout(LayoutKind.Sequential)]
  public record struct CGPoint(double X, double Y);

  [StructLayout(LayoutKind.Sequential)]
  public record struct CGSize(double Width, double Height);

  [StructLayout(LayoutKind.Sequential)]
  public record struct CGRect(CGPoint Origin, CGSize Size)
  {
    public CGRect(double x, double y, double width, double height) : this(
      new CGPoint(x, y), new CGSize(width, height))
    {
    }
  }

  /// <summary>
  /// struct objc_super: the receiver and the class whose superclass's method runs.
  /// </summary>
  [StructLayout(LayoutKind.Sequential)]
  public struct ObjCSuper
  {
    public IntPtr Receiver;
    public IntPtr SuperClass;
  }

  [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
  public static partial IntPtr objc_getClass(string name);

  [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
  public static partial IntPtr sel_registerName(string name);

  [LibraryImport(ObjC)]
  public static partial IntPtr sel_getName(IntPtr selector);

  [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
  public static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name,
                                                      nuint extraBytes);

  [LibraryImport(ObjC)]
  public static partial void objc_registerClassPair(IntPtr cls);

  [LibraryImport(ObjC)]
  public static partial IntPtr class_getInstanceMethod(IntPtr cls, IntPtr selector);

  [LibraryImport(ObjC)]
  public static partial IntPtr method_getTypeEncoding(IntPtr method);

  [LibraryImport(ObjC)]
  [return: MarshalAs(UnmanagedType.U1)]
  public static partial bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation,
                                             IntPtr types);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial IntPtr Send(IntPtr receiver, IntPtr selector);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial IntPtr Send(IntPtr receiver, IntPtr selector, CGRect rect);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial IntPtr Send(IntPtr receiver, IntPtr selector, CGRect rect, nuint options,
                                    IntPtr owner, IntPtr userInfo);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial IntPtr Send(IntPtr receiver, IntPtr selector, CGRect rect, nuint style,
                                    nuint backing, [MarshalAs(UnmanagedType.U1)] bool defer);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial void Send(IntPtr receiver, IntPtr selector,
                                  [MarshalAs(UnmanagedType.U1)] bool value);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial nuint SendUInt(IntPtr receiver, IntPtr selector);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial double SendDouble(IntPtr receiver, IntPtr selector);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial CGPoint SendPoint(IntPtr receiver, IntPtr selector);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial CGPoint SendPoint(IntPtr receiver, IntPtr selector, CGPoint point);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial CGPoint SendPoint(IntPtr receiver, IntPtr selector, CGPoint point,
                                          IntPtr view);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial CGRect SendRect(IntPtr receiver, IntPtr selector);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
  public static partial CGSize SendSize(IntPtr receiver, IntPtr selector, CGSize size);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSendSuper")]
  public static partial void SendSuper(in ObjCSuper super, IntPtr selector);

  [LibraryImport(ObjC, EntryPoint = "objc_msgSendSuper")]
  public static partial void SendSuper(in ObjCSuper super, IntPtr selector, CGSize size);

  /// <summary>A class by name; throws when the runtime has none (AppKit not loaded).</summary>
  public static IntPtr Class(string name)
  {
    var cls = objc_getClass(name);
    return cls != IntPtr.Zero
      ? cls
      : throw new InvalidOperationException($"no Objective-C class {name}");
  }

  public static IntPtr Selector(string name) => sel_registerName(name);
}
