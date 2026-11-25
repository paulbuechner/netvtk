// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

namespace NetVtk.Viewer.Tests;

/// <summary>
/// The assembly's entry point, in place of the test SDK's empty one, for the cases that run in a
/// process of their own: <c>cocoa</c> (<see cref="CocoaHost"/>) and <c>main-thread &lt;test&gt;</c>
/// (<see cref="MainThreadRun"/>).
/// </summary>
internal static class Program
{
  private static int Main(string[] args) => args switch
  {
    [CocoaHost.Command] => CocoaHost.Run(),
    [MainThreadRun.Command, var test] => MainThreadRun.Run(test),
    _ => 2,
  };
}
