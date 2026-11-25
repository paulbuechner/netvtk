// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;

// NUnit
using NUnit.Framework;

//
using NetVtk.Generator.Config;

namespace NetVtk.Generator.Tests;

/// <summary>
/// config/modules.yaml's platform libraries and modules of another module's headers, over a fake
/// VTK install whose module Demo installs one platform's headers.
/// </summary>
[TestFixture]
public class GeneratorConfigTests
{
  private const string Libraries = """
    libraries:
      Demo: [Demo]
      Win32: [DemoWin32]
    platforms:
      Win32: win
    exclude_headers: [vtkWin32*.h]

    """;

  private TempDirectory _work = null!;

  [SetUp]
  public void CreateVtk()
  {
    _work = new TempDirectory("netvtk-gen-config");
    _work.Write("vtk/include/vtk-9.7/vtkVersionMacros.h", "#define VTK_VERSION \"9.7.1\"\n");
    _work.Write("vtk/share/vtk/VTK-vtk-module-properties.cmake",
                "set_property(TARGET \"VTK::Demo\" PROPERTY \"INTERFACE_vtk_module_headers\" "
                + "\"vtkDemo.h;vtkWin32DemoOutput.h;vtkWin32DemoWindow.h\")\n");
  }

  [TearDown]
  public void DeleteVtk() => _work.Dispose();

  [Test]
  public void Headers_ModuleOfAnothersHeaders_TakesThoseThatMatch()
  {
    // Arrange (exclude_headers matches them too)
    var workspace = WorkspaceWith("DemoWin32: {vtk_module: Demo, headers: [vtkWin32*Window.h]}");

    // Act
    var (headers, excluded) = workspace.Headers("DemoWin32");

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(headers, Is.EqualTo(new[] { "vtkWin32DemoWindow.h" }));
      Assert.That(excluded, Is.Empty);
    }
  }

  [Test]
  public void Headers_TakenHeader_LeavesItsOwnModuleUnlogged()
  {
    // Arrange
    var workspace = WorkspaceWith("DemoWin32: {vtk_module: Demo, headers: [vtkWin32*Window.h]}");

    // Act
    var (headers, excluded) = workspace.Headers("Demo");

    // Assert (the one no module takes is exclude_headers')
    using (Assert.EnterMultipleScope())
    {
      Assert.That(headers, Is.EqualTo(new[] { "vtkDemo.h" }));
      Assert.That(excluded, Is.EqualTo(new[] { "vtkWin32DemoOutput.h" }));
    }
  }

  [Test]
  public void Expand_ModuleOfAnothersHeaders_IsAModule()
  {
    // Arrange
    var workspace = WorkspaceWith("DemoWin32: {vtk_module: Demo, headers: [vtkWin32*Window.h]}");

    // Act
    var modules = workspace.Expand(["DemoWin32", "Win32", "Demo"]);

    // Assert
    Assert.That(modules, Is.EqualTo(new[] { "DemoWin32", "Demo" }));
  }

  [Test]
  public void Workspace_ModuleTakingNoHeader_Fails()
  {
    // Act (VTK renamed the header, or doesn't install it here)
    Action create = () => WorkspaceWith("DemoWin32: {vtk_module: Demo, headers: [vtkWin32Gone.h]}");

    // Assert
    Assert.That(
      create,
      Throws.InstanceOf<InvalidDataException>()
        .With.Message.EqualTo("config: DemoWin32: no header of Demo matches its headers"));
  }

  [Test]
  public void Workspace_VtkModuleVtkLacks_Fails()
  {
    // Act
    Action create = () => WorkspaceWith("DemoWin32: {vtk_module: Gone, headers: [vtkWin32*.h]}");

    // Assert
    Assert.That(
      create,
      Throws.InstanceOf<KeyNotFoundException>()
        .With.Message.EqualTo("config: DemoWin32: VTK has no module Gone (vtk_module)"));
  }

  [Test]
  public void Load_PlatformOfNoLibrary_Fails()
  {
    // Arrange
    var path = _work.Write("modules.yaml",
                           "libraries:\n  Demo: [Demo]\nplatforms:\n  Win32: win\n");

    // Act
    Action load = () => GeneratorConfig.Load(path);

    // Assert
    Assert.That(
      load,
      Throws.InstanceOf<InvalidDataException>()
        .With.Message.EndsWith(": platforms: Win32 isn't a library"));
  }

  [Test]
  public void Load_OperatingSystemNoRidNames_Fails()
  {
    // Arrange
    var path = _work.Write("modules.yaml",
                           "libraries:\n  Win32: [Demo]\nplatforms:\n  Win32: windows\n");

    // Act
    Action load = () => GeneratorConfig.Load(path);

    // Assert
    Assert.That(
      load,
      Throws.InstanceOf<InvalidDataException>()
        .With.Message.EndsWith(": platforms: Win32: windows isn't one of win, linux, osx"));
  }

  [Test]
  public void Load_VtkModuleWithoutHeaders_Fails()
  {
    // Arrange
    var path = _work.Write("modules.yaml",
                           "libraries:\n  Win32: [DemoWin32]\nmodules:\n  DemoWin32:\n    vtk_module: Demo\n");

    // Act
    Action load = () => GeneratorConfig.Load(path);

    // Assert
    Assert.That(
      load,
      Throws.InstanceOf<InvalidDataException>()
        .With.Message.EndsWith(": modules: DemoWin32: vtk_module and headers go together"));
  }

  [Test]
  public void PlatformsJson_ListsThePlatformLibrariesInLibraryOrder()
  {
    // Arrange
    var config = new GeneratorConfig
    {
      Libraries = new() { ["Common"] = ["CommonCore"], ["X11"] = ["A"], ["Win32"] = ["B"] },
      Platforms = new() { ["Win32"] = "win", ["X11"] = "linux" },
    };

    // Act
    var json = Generator.Generate.PlatformsJson(config);

    // Assert
    Assert.That(json, Is.EqualTo("{\n  \"X11\": \"linux\",\n  \"Win32\": \"win\"\n}\n"));
  }

  [Test]
  public void PlatformsJson_NoPlatforms_IsAnEmptyObject()
  {
    // Arrange
    var config = new GeneratorConfig { Libraries = new() { ["Common"] = ["CommonCore"] } };

    // Act
    var json = Generator.Generate.PlatformsJson(config);

    // Assert
    Assert.That(json, Is.EqualTo("{}\n"));
  }

  // the libraries above and modules: one entry
  private Workspace WorkspaceWith(string module) =>
    new(_work.Write("config/modules.yaml", $"{Libraries}modules:\n  {module}\n"),
        _work.Combine("vtk"));
}
