// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.IO;
using System.Threading.Tasks;

// NUnit
using NUnit.Framework;

namespace NetVtk.Viewer.Tests;

/// <summary>
/// The opener: a sample that reads, a file that doesn't as a status, one open at a time.
/// </summary>
[TestFixture]
public class FileOpenerTests
{
  [Test]
  public async Task OpenSample_ReadsIt()
  {
    // Arrange
    var opener = new FileOpener();

    // Act
    var result = await opener.OpenSampleAsync(_ => { });

    // Assert
    using var document = result.Document;
    using (Assert.EnterMultipleScope())
    {
      Assert.That(document, Is.Not.Null);
      Assert.That(result.Status, Does.Contain("1280 cells"));
    }
  }

  [Test]
  public async Task Open_Missing_IsAStatus()
  {
    // Arrange
    var opener = new FileOpener();
    var path = Path.Combine(Path.GetTempPath(), "netvtk-viewer-tests-missing.vtu");

    // Act
    var result = await opener.OpenAsync(path, _ => { });

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(result.Document, Is.Null);
      Assert.That(result.Status, Does.StartWith("netvtk-viewer-tests-missing.vtu: "));
    }
  }

  [Test]
  public async Task Open_WhileOpening_IsRefused()
  {
    // Arrange
    var opener = new FileOpener();
    var first = opener.OpenSampleAsync(_ => { });

    // Act
    var second = await opener.OpenSampleAsync(_ => { });

    // Assert
    using var document = (await first).Document;
    Assert.That(second.Status, Is.EqualTo("Another file is still opening."));
  }
}
