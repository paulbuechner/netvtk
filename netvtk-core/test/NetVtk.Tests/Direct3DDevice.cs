// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// A Direct3D 11 device and its immediate context on the default adapter, as an application that
/// shares textures with VTK's render window has them (d3d11.dll's own function, no package). The
/// window takes references of its own; dispose releases these.
/// </summary>
internal sealed class Direct3DDevice : IDisposable
{
  private const int DriverTypeHardware = 1;
  private const uint CreateDeviceBgraSupport = 0x20;
  private const uint SdkVersion = 7;

  // DXGI_FORMAT_R8G8B8A8_UNORM, VTK's colour texture format
  private const uint Rgba8 = 28;
  private const uint UsageDefault = 0;
  private const uint UsageStaging = 3;
  private const uint BindShaderResource = 0x8;
  private const uint BindRenderTarget = 0x20;
  private const uint CpuAccessRead = 0x20000;
  private const int MapRead = 1;

  // the methods' places in the COM interfaces' tables (d3d11.h)
  private const int CreateTexture2DSlot = 5;
  private const int MapSlot = 14;
  private const int UnmapSlot = 15;
  private const int CopyResourceSlot = 47;

  private static bool? _interop;

  private Direct3DDevice(IntPtr device, IntPtr context)
  {
    Device = device;
    Context = context;
  }

  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  private delegate int CreateTexture2D(IntPtr device, ref TextureDescription description,
                                       IntPtr initialData, out IntPtr texture);

  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  private delegate int Map(IntPtr context, IntPtr resource, uint subresource, int type, uint flags,
                           out MappedSubresource mapped);

  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  private delegate void Unmap(IntPtr context, IntPtr resource, uint subresource);

  [UnmanagedFunctionPointer(CallingConvention.StdCall)]
  private delegate void CopyResource(IntPtr context, IntPtr destination, IntPtr source);

  /// <summary>The <c>ID3D11Device*</c>.</summary>
  public IntPtr Device { get; private set; }

  /// <summary>The device's immediate context, an <c>ID3D11DeviceContext*</c>.</summary>
  public IntPtr Context { get; private set; }

  /// <summary>The device's reference count: ours and the ones others took.</summary>
  public int References
  {
    get
    {
      Marshal.AddRef(Device);
      return Marshal.Release(Device);
    }
  }

  /// <summary>
  /// Whether the OpenGL driver shares textures with Direct3D (WGL_NV_DX_interop): a DX window on a
  /// device of this machine's gets a shared texture. Probed once.
  /// </summary>
  public static bool Interop
  {
    get
    {
      if (_interop == null)
      {
        using var device = Create();
        using var window = new vtkWin32OpenGLDXRenderWindow();
        window.ErrorEvent += (sender, _) => (sender as IDisposable)?.Dispose();
        window.SetShowWindow(false);
        window.SetD3DDeviceContext(device.Context);
        window.Initialize();
        _interop = window.GetD3DSharedTexture() != IntPtr.Zero;
      }

      return _interop.Value;
    }
  }

  public static Direct3DDevice Create()
  {
    Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, DriverTypeHardware, IntPtr.Zero,
                                                  CreateDeviceBgraSupport, null, 0, SdkVersion,
                                                  out var device, out _, out var context));
    return new Direct3DDevice(device, context);
  }

  /// <summary>
  /// A texture of one sample per pixel in VTK's colour format, an <c>ID3D11Texture2D*</c> the
  /// caller releases (<see cref="Release"/>): what an application blits a window's frame into.
  /// </summary>
  public IntPtr CreateTexture(int width, int height) =>
    NewTexture(width, height, UsageDefault, BindShaderResource | BindRenderTarget, 0);

  /// <summary>A texture of <see cref="CreateTexture"/>'s, its RGBA rows from the bottom up, as
  /// OpenGL reads them.</summary>
  public byte[] ReadPixels(IntPtr texture, int width, int height)
  {
    var staging = NewTexture(width, height, UsageStaging, 0, CpuAccessRead);
    try
    {
      Method<CopyResource>(Context, CopyResourceSlot)(Context, staging, texture);
      Marshal.ThrowExceptionForHR(
        Method<Map>(Context, MapSlot)(Context, staging, 0, MapRead, 0, out var mapped));
      var rgba = new byte[4 * width * height];
      for (var row = 0; row < height; row++)
      {
        var source = new IntPtr(mapped.Data.ToInt64() + (long)row * mapped.RowPitch);
        Marshal.Copy(source, rgba, 4 * width * (height - 1 - row), 4 * width);
      }

      Method<Unmap>(Context, UnmapSlot)(Context, staging, 0);
      return rgba;
    }
    finally
    {
      Release(staging);
    }
  }

  public void Dispose()
  {
    Release(Context);
    Release(Device);
    Context = Device = IntPtr.Zero;
  }

  public static void Release(IntPtr unknown)
  {
    if (unknown != IntPtr.Zero)
    {
      Marshal.Release(unknown);
    }
  }

  private IntPtr NewTexture(int width, int height, uint usage, uint bind, uint cpuAccess)
  {
    var description = new TextureDescription
    {
      Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = Rgba8,
      SampleCount = 1, Usage = usage, BindFlags = bind, CpuAccessFlags = cpuAccess,
    };
    Marshal.ThrowExceptionForHR(Method<CreateTexture2D>(Device, CreateTexture2DSlot)(
                                  Device, ref description, IntPtr.Zero, out var texture));
    return texture;
  }

  // a COM method by its place in the interface's table
  private static T Method<T>(IntPtr unknown, int slot) where T : class
  {
    var table = Marshal.ReadIntPtr(unknown);
    var method = Marshal.ReadIntPtr(table, slot * IntPtr.Size);
    return (T)(object)Marshal.GetDelegateForFunctionPointer(method, typeof(T));
  }

  [DllImport("d3d11.dll", ExactSpelling = true)]
  private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software,
                                              uint flags, int[]? featureLevels,
                                              uint featureLevelCount, uint sdkVersion,
                                              out IntPtr device, out int featureLevel,
                                              out IntPtr immediateContext);

  // D3D11_TEXTURE2D_DESC
  [StructLayout(LayoutKind.Sequential)]
  private struct TextureDescription
  {
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public uint Format;
    public uint SampleCount;
    public uint SampleQuality;
    public uint Usage;
    public uint BindFlags;
    public uint CpuAccessFlags;
    public uint MiscFlags;
  }

  // D3D11_MAPPED_SUBRESOURCE
  [StructLayout(LayoutKind.Sequential)]
  private struct MappedSubresource
  {
    public IntPtr Data;
    public uint RowPitch;
    public uint DepthPitch;
  }
}
