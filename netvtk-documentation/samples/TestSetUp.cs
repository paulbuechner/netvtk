// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Samples;

/// <summary>
/// VTK's messages come to C# for every sample: VTK's own output window would open a window on
/// Windows with the first error a sample provokes.
/// </summary>
[SetUpFixture]
public class TestSetUp
{
  [OneTimeSetUp]
  public void RouteVtkOutput() => VtkOutput.Install();
}
