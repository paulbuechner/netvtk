// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#if NET5_0_OR_GREATER
namespace NetVtk.Tests;

/// <summary>
/// The assembly's entry point on .NET, in place of the test SDK's empty one, for the cases that run
/// in a process of their own: <c>first &lt;class&gt;</c> (<see cref="FirstObject"/>) and
/// <c>main-thread &lt;test&gt;</c> (<see cref="MainThreadRun"/>).
/// </summary>
internal static class Program
{
  private static int Main(string[] args) => args switch
  {
    [FirstObject.Command, var className] => FirstObject.Make(className),
    [MainThreadRun.Command, var test] => MainThreadRun.Run(test),
    _ => 2,
  };
}
#endif
