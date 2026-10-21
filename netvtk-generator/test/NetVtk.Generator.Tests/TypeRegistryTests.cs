// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.IO;
using System.Runtime.CompilerServices;

// NUnit
using NUnit.Framework;

//
using NetVtk.Generator.Mapping;

namespace NetVtk.Generator.Tests;

/// <summary>What a run knows of a module it doesn't write, from the module's .i file.</summary>
[TestFixture]
public class TypeRegistryTests
{
  [Test]
  public void ScanModule_KnowsWhatARunWritingTheModuleKnows()
  {
    // Arrange (the golden run's Demo module, as a later run over other packages finds it)
    using var work = new TempDirectory("netvtk-gen-scan");
    var expected = Path.Combine(SourceDirectory(), "Golden", "expected");
    var wrapper = work.CreateSubdirectory("wrapper");
    var headers = work.CreateSubdirectory("headers");
    File.Copy(Path.Combine(expected, "Demo.i"), Path.Combine(wrapper, "Demo.i"));
    File.Copy(Path.Combine(expected, "Demo_nested.hxx"), Path.Combine(headers, "Demo_nested.hxx"));
    var registry = new TypeRegistry();

    // Act
    registry.ScanModule(Path.Combine(wrapper, "Demo.i"));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(registry.Enum("vtkDemoSource_Notation"),
                  Is.EqualTo(new KnownEnum("vtkDemoSource_Notation", "Demo", "Demo_nested.hxx",
                                           QualifiedName: "vtkDemoSource::Notation")),
                  "the nested header's elaborated alias");
      Assert.That(registry.Enum("vtkDemoMode")?.Package, Is.EqualTo("Demo"));
      Assert.That(registry.Enum("class"), Is.Null, "enum class X is X");
      Assert.That(registry.Class("vtkDemoTemplate_double")?.DeclaringHeader,
                  Is.EqualTo("Demo_nested.hxx"));
      Assert.That(registry.Class("vtkDemoSource"),
                  Is.EqualTo(new KnownClass("vtkDemoSource", "Demo", WrapKind.VtkObject)));
      Assert.That(registry.Class("vtkDemoSource")?.DeclaringHeader, Is.EqualTo("vtkDemoSource.h"));
      Assert.That(registry.Class("vtkDemoInfo")?.Kind, Is.EqualTo(WrapKind.ValueType));
      Assert.That(registry.ImportsOf("Demo"), Is.EqualTo(new[] { "Core" }));
    }
  }

  // the fixture lives next to this file in the source tree
  private static string SourceDirectory([CallerFilePath] string path = "") =>
    Path.GetDirectoryName(path)!;
}
