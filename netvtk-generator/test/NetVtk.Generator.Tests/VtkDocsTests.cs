// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

// NUnit
using NUnit.Framework;

//
using NetVtk.Generator.Parsing;

namespace NetVtk.Generator.Tests;

/// <summary>
/// netvtk-gen docs on VTK's layout of comments: a class's comment at the top of its header, before
/// the include guard, and members documented as a group. Our own text: VTK's stays out.
/// </summary>
[TestFixture]
public class VtkDocsTests
{
  private const string Header = """
    /**
     * @class   demoGrid
     * @brief   a grid of demo points
     *
     * Its second paragraph.
     */
    #ifndef demoGrid_h
    #define demoGrid_h

    class demoGrid
    {
    public:
      ///@{
      /**
       * Set/Get the spacing.
       */
      void SetSpacing(double spacing);
      double GetSpacing();
      ///@}

      /**
       * The number of points and cells.
       * @note Counted once.
       */
      ///@{
      int GetNumberOfPoints();
      int GetNumberOfCells();
      ///@}

      int Undocumented();
    };

    #endif
    """;

  private Dictionary<string, TypeDocs> _docs = null!;

  [OneTimeSetUp]
  public void Parse()
  {
    using var directory = new TempDirectory("netvtk-gen-vtkdocs");
    directory.Write("demoGrid.h", Header);
    _docs = new PackageParser([directory.FullName], []).ParseDocs("Demo", ["demoGrid.h"], []);
  }

  [Test]
  public void ParseDocs_TakesTheClassCommentAboveTheIncludeGuard()
  {
    // Act
    var xml = _docs["demoGrid"].Xml;

    // Assert (the @class line names the class: no text of its own)
    Assert.That(xml, Is.EqualTo(
                  "<summary>a grid of demo points</summary><remarks><para>Its second paragraph.</para></remarks>"));
  }

  [Test]
  public void ParseDocs_GivesAGroupsCommentToEachOfItsMembers()
  {
    // Act
    var members = _docs["demoGrid"].Members;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(members.Find(m => m.Name == "SetSpacing")?.Xml,
                  Is.EqualTo("<summary>Set/Get the spacing.</summary>"));
      Assert.That(members.Find(m => m.Name == "GetSpacing")?.Xml,
                  Is.EqualTo("<summary>Set/Get the spacing.</summary>"));
    }
  }

  [Test]
  public void ParseDocs_EndsTheGroupAtItsMarkerAndDropsTheMarkersFromTheText()
  {
    // Act
    var members = _docs["demoGrid"].Members;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(members.Find(m => m.Name == "GetNumberOfCells")?.Xml,
                  Is.EqualTo(
                    "<summary>The number of points and cells.</summary><remarks><para>Note: Counted once.</para></remarks>"));
      Assert.That(members.Exists(m => m.Name == "Undocumented"), Is.False);
    }
  }
}
