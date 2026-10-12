// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace NetVtk.Generator.Parsing;

/// <summary>
/// The C++ standard library's headers for libclang on Windows: it reads them from <c>INCLUDE</c>,
/// which a Visual Studio developer prompt sets and other shells don't (and libclang doesn't find
/// Visual Studio 2026 by itself). Without it every VTK header fails on <c>&lt;fstream&gt;</c>.
/// </summary>
internal static class MsvcEnvironment
{
  /// <summary>
  /// Sets <c>INCLUDE</c> for this process from the newest Visual Studio with the C++ tools
  /// (vcvarsall x64), unless it is set; nothing off Windows.
  /// </summary>
  public static void Ensure()
  {
    if (!OperatingSystem.IsWindows()
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INCLUDE")))
    {
      return;
    }

    var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                               "Microsoft Visual Studio", "Installer", "vswhere.exe");
    if (!File.Exists(vswhere))
    {
      throw new InvalidOperationException(
        $"{vswhere} missing: install Visual Studio with the C++ tools, or run from a developer prompt (INCLUDE)");
    }

    var install =
      Run(vswhere,
          "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath")
        .Trim();
    var vcvars = Path.Combine(install, "VC", "Auxiliary", "Build", "vcvarsall.bat");
    if (install.Length == 0 || !File.Exists(vcvars))
    {
      throw new InvalidOperationException(
        "no Visual Studio with the C++ tools: install them, or run from a developer prompt (INCLUDE)");
    }

    var include = Run("cmd.exe", $"/c \"\"{vcvars}\" x64 >nul && set INCLUDE\"")
      .Split('\n')
      .Select(l => l.Trim())
      .FirstOrDefault(l => l.StartsWith("INCLUDE=", StringComparison.OrdinalIgnoreCase));
    if (include is null)
    {
      throw new InvalidOperationException($"{vcvars} x64 set no INCLUDE");
    }

    Environment.SetEnvironmentVariable("INCLUDE", include["INCLUDE=".Length..]);
  }

  private static string Run(string program, string arguments)
  {
    using var process = Process.Start(new ProcessStartInfo(program, arguments)
                        {
                          RedirectStandardOutput = true, RedirectStandardError = true,
                          UseShellExecute = false
                        })
                        ?? throw new InvalidOperationException($"{program} didn't start");
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return output;
  }
}
