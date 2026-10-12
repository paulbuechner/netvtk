// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

// NUnit
using NUnit.Framework;

namespace NetVtk.Generator.Tests;

/// <summary>
/// A command's options, flags and names, strictly: an option it doesn't take is an error.
/// </summary>
[TestFixture]
public class CommandLineTests
{
  [Test]
  public void Parse_TellsOptionsFlagsAndNamesApart()
  {
    // Arrange
    string[] arguments = ["--vtk", "vtk", "CommonCore", "--check", "FiltersCore"];

    // Act
    var line = CommandLine.Parse("generate", arguments, ["--vtk", "--core"], ["--check"]);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(line.Option("--vtk"), Is.EqualTo("vtk"));
      Assert.That(line.Option("--core"), Is.Null);
      Assert.That(line.Flag("--check"), Is.True);
      Assert.That(line.Names, Is.EqualTo(new[] { "CommonCore", "FiltersCore" }));
    }
  }

  [Test]
  public void Parse_UnknownOption_Fails()
  {
    // Act (a typo mustn't fall back to a default unnoticed)
    Action parse = () => CommandLine.Parse("generate", ["--vtk-prefix", "vtk"], ["--vtk"]);

    // Assert
    Assert.That(
      parse,
      Throws.ArgumentException.With.Message.EqualTo(
        "netvtk-gen generate: unknown option --vtk-prefix"));
  }

  [Test]
  public void Parse_OptionFollowedByAnOption_Fails()
  {
    // Act (--check is a flag, not --vtk's value)
    Action parse = () =>
      CommandLine.Parse("generate", ["--vtk", "--check"], ["--vtk"], ["--check"]);

    // Assert
    Assert.That(
      parse,
      Throws.ArgumentException.With.Message.EqualTo("netvtk-gen generate: --vtk needs a value"));
  }

  [Test]
  public void Required_MissingOption_Fails()
  {
    // Arrange
    var line = CommandLine.Parse("docs", [], ["--out"]);

    // Act
    Action required = () => line.Required("--out");

    // Assert
    Assert.That(
      required,
      Throws.ArgumentException.With.Message.EqualTo("netvtk-gen docs: --out is required"));
  }
}
