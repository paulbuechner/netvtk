// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// NUnit
using NUnit.Framework;

namespace NetVtk.Samples;

/// <summary>
/// The READMEs show tested examples, which a README can't include: the repository's (README.md)
/// the start page's program (Programs/Contours.cs, region program, which Programs runs), the NetVtk
/// package's (netvtk-core/src/NetVtk/README.md, on nuget.org) the getting-started cone
/// (GettingStarted.cs, regions usings and cone). The csproj copies the files next to the tests.
/// </summary>
[TestFixture]
public class Readmes
{
  private static readonly string Copies =
    Path.Combine(TestContext.CurrentContext.TestDirectory, "Readmes");

  [Test]
  public void RepositoryReadme_ShowsTheContoursProgram()
  {
    // Arrange
    var readme = Read("Repository.md");
    var program = Region(Read("Contours.cs"), "program");

    // Act
    var example = Example(readme);

    // Assert
    Assert.That(example, Is.EqualTo(program),
                "README.md's example differs from Programs/Contours.cs: copy its region program "
                + "into it");
  }

  [Test]
  public void PackageReadme_ShowsTheGettingStartedSample()
  {
    // Arrange
    var readme = Read("Package.md");
    var sample = Read("GettingStarted.cs");

    // Act
    var example = Example(readme);

    // Assert
    Assert.That(example, Is.EqualTo($"{Region(sample, "usings")}\n\n{Region(sample, "cone")}"),
                "the package README's example differs from GettingStarted.cs: copy the regions "
                + "usings and cone into it");
  }

  private static string Read(string name) =>
    File.ReadAllText(Path.Combine(Copies, name)).Replace("\r\n", "\n");

  // the first C# code block
  private static string Example(string readme) => Regex
    .Match(readme, "```csharp\n(.*?)\n```", RegexOptions.Singleline)
    .Groups[1].Value;

  // a region's lines without their common indentation, as the documentation shows them
  private static string Region(string source, string name)
  {
    var lines = Regex
      .Match(source, $@"#region {Regex.Escape(name)}\n(.*?)\n[ \t]*#endregion",
             RegexOptions.Singleline)
      .Groups[1]
      .Value.Split('\n');
    var indent = lines.Where(line => line.Trim().Length > 0)
      .Min(line => line.Length - line.TrimStart().Length);
    return string.Join(
      "\n",
      lines.Select(line => line.Length >= indent ? line.Substring(indent) : line.TrimStart()));
  }
}
