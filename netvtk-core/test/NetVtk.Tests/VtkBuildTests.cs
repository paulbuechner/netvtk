// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

//
using Vtk;

namespace NetVtk.Tests;

/// <summary>
/// The VTK build NetVtk's natives carry (the port's vtkNetVtkBuild.h), which a binding compiled
/// against VTK's headers compares with its own when it loads.
/// </summary>
[TestFixture]
public class VtkBuildTests
{
  [Test]
  public void Current_IsTheLoadedVersionAndThePortVersion()
  {
    // Act
    var build = VtkBuild.Current;

    // Assert
    Assert.That(
      build, Does.Match(@"^\d+\.\d+\.\d+#\d+$").And.StartWith(vtkVersion.GetVTKVersion() + "#"));
  }

  [Test]
  public void Require_TheSameBuild_Passes()
  {
    // Act
    Action require = () => VtkBuild.Require(VtkBuild.Current, "Demo");

    // Assert
    Assert.That(() => require(), Throws.Nothing);
  }

  [Test]
  public void Require_AnotherBuild_ThrowsNamingTheBindingAndBothBuilds()
  {
    // Act
    Action require = () => VtkBuild.Require("9.7.1#0", "Demo");

    // Assert
    Assert.That(() => require(),
                Throws.InvalidOperationException.With.Message.Contains("Demo")
                  .And.Message.Contains("9.7.1#0")
                  .And.Message.Contains(VtkBuild.Current));
  }
}
