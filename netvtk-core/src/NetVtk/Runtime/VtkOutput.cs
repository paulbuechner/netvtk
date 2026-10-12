// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;

namespace Vtk;

/// <summary>What a VTK message reports.</summary>
public enum VtkOutputKind
{
  /// <summary>Text (<c>vtkOutputWindowDisplayText</c>).</summary>
  Text,

  /// <summary>An error (<c>vtkErrorMacro</c>).</summary>
  Error,

  /// <summary>A warning (<c>vtkWarningMacro</c>).</summary>
  Warning,

  /// <summary>A warning without an object (<c>vtkGenericWarningMacro</c>).</summary>
  GenericWarning,

  /// <summary>Debug output (<c>vtkDebugMacro</c>, objects with <c>DebugOn()</c>).</summary>
  Debug,
}

/// <summary>A message VTK reported.</summary>
public sealed class VtkOutputEventArgs(VtkOutputKind kind, string text) : EventArgs
{
  /// <summary>What the message reports.</summary>
  public VtkOutputKind Kind { get; } = kind;

  /// <summary>The message, as VTK formats it (an error names its source file and line).</summary>
  public string Text { get; } = text;
}

/// <summary>
/// VTK's messages (errors, warnings, text) in C#. VTK writes them to its output window: on Windows
/// a window of its own that opens with the first message, elsewhere the console. Once
/// <see cref="Install"/>ed, or with a handler on <see cref="Message"/>, they come here instead, and
/// go to <see cref="Trace"/> while no handler is attached. An object with an <c>ErrorEvent</c> or
/// <c>WarningEvent</c> observer reports to it instead (<see cref="vtkObject.ErrorEvent"/>).
/// </summary>
public static class VtkOutput
{
  private static readonly object Sync = new();
  private static OutputWindow _window;
  private static EventHandler<VtkOutputEventArgs> _message;
  private static bool _unloadHooked;

  /// <summary>
  /// A message VTK reported, on the thread that reported it; adding a handler installs the
  /// routing. An exception a handler throws comes out of the VTK call that reported the message.
  /// </summary>
  public static event EventHandler<VtkOutputEventArgs> Message
  {
    add
    {
      lock (Sync)
      {
        _message += value;
      }

      Install();
    }
    remove
    {
      lock (Sync)
      {
        _message -= value;
      }
    }
  }

  /// <summary>Whether VTK's messages come to C#.</summary>
  public static bool IsInstalled
  {
    get
    {
      lock (Sync)
      {
        return _window is not null;
      }
    }
  }

  /// <summary>
  /// Routes VTK's messages to <see cref="Message"/> (VTK's output window instance).
  /// </summary>
  public static void Install()
  {
    lock (Sync)
    {
      if (_window is not null)
      {
        return;
      }

      _window = new OutputWindow();
      vtkOutputWindow.SetInstance(_window);
      // VTK keeps the instance past C#: it reports while objects go at the end, and a callback
      // into an AppDomain .NET Framework unloads hangs the unload. VTK gets its own back first.
      if (!_unloadHooked)
      {
        _unloadHooked = true;
        AppDomain.CurrentDomain.DomainUnload += (_, _) => Uninstall();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Uninstall();
      }
    }
  }

  /// <summary>Gives VTK its own output window back.</summary>
  public static void Uninstall()
  {
    lock (Sync)
    {
      if (_window is null)
      {
        return;
      }

      vtkOutputWindow.SetInstance(null);
      _window.Dispose();
      _window = null;
    }
  }

  private static void Report(VtkOutputKind kind, string text)
  {
    EventHandler<VtkOutputEventArgs> handlers;
    lock (Sync)
    {
      handlers = _message;
    }

    if (handlers is null)
    {
      Trace.WriteLine(text?.TrimEnd(), $"VTK {kind}");
      return;
    }

    handlers(null, new VtkOutputEventArgs(kind, text));
  }

  // VTK's output window, overridden: no console, no window, every message to Report
  [CSharpOverrides]
  private sealed class OutputWindow : vtkOutputWindow
  {
    public override void DisplayText(string text) => Report(VtkOutputKind.Text, text);

    public override void DisplayErrorText(string text) => Report(VtkOutputKind.Error, text);

    public override void DisplayWarningText(string text) => Report(VtkOutputKind.Warning, text);

    public override void DisplayGenericWarningText(string text) =>
      Report(VtkOutputKind.GenericWarning, text);

    public override void DisplayDebugText(string text) => Report(VtkOutputKind.Debug, text);
  }
}
