// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using NetVtk.Generator.Emit;
using NetVtk.Generator.Model;

// static usings
using static NetVtk.Generator.Tests.Types;

namespace NetVtk.Generator.Tests;

/// <summary>
/// VTK's size hints that name a parameter: the count each one gives, as the member's SWIG wrapper
/// spells it.
/// </summary>
[TestFixture]
public class SizeHintsTests
{
  // vtkIdType, as the parser has it
  private static readonly BuiltinType IdType = new("long long", Written: "vtkIdType");

  // GetCellPoints(vtkIdType cellId, vtkIdType& npts, const vtkIdType*& pts), with a hint
  private static MethodModel CellPoints(string hint, bool isStatic = false) =>
    new("GetCellPoints", Builtin("void"), [
      new ParameterModel("cellId", IdType, null), new ParameterModel("npts", Ref(IdType), null),
      new ParameterModel("pts", Ref(Pointer(Const(IdType))), null)
    ], isStatic, IsConst: false, IsVirtual: false, IsDeprecated: false, Hints: [hint]);

  [Test]
  public void Of_AReference_CountsItsValueAfterTheCall()
  {
    // Arrange (the wrapper's object is arg1, npts arg3, a pointer to the caller's value)
    var m = CellPoints("vtk::sizehint(pts, npts)");

    // Act
    var sized = SizeHints.Of(m, m);

    // Assert
    Assert.That(sized, Is.EqualTo(new Dictionary<int, string?> { [2] = "(*arg3)" }));
  }

  [Test]
  public void Of_AnExpression_IsInTheWrappersTerms()
  {
    // Arrange (a static member has no object: npts is arg2)
    var m = CellPoints("vtk::sizehint(pts, 3 * npts)", isStatic: true);

    // Act
    var sized = SizeHints.Of(m, m);

    // Assert
    Assert.That(sized[2], Is.EqualTo("3 * (*arg2)"));
  }

  [Test]
  public void Of_TheResult_IsTheWrappersResult()
  {
    // Arrange (_ is the member's result)
    var m = CellPoints("vtk::sizehint(pts, _)") with { Return = IdType };

    // Act
    var sized = SizeHints.Of(m, m);

    // Assert
    Assert.That(sized[2], Is.EqualTo("result"));
  }

  [TestCase("vtk::sizehint(pts, GetNumberOfPoints())")]
  [TestCase("vtk::sizehint(pts, _)")]
  public void Of_ACountTheWrapperCantSpell_IsNull(string hint)
  {
    // Arrange (a call of another member; the result of a member returning void)
    var m = CellPoints(hint);

    // Act
    var sized = SizeHints.Of(m, m);

    // Assert
    Assert.That(sized, Is.EqualTo(new Dictionary<int, string?> { [2] = null }));
  }

  [Test]
  public void Of_AHintOfTheMembersOwnLength_NamesNoParameter()
  {
    // Arrange (a returned pointer's length: SignatureMapper.Sized)
    var m = CellPoints("vtk::sizehint(6)");

    // Act
    var sized = SizeHints.Of(m, m);

    // Assert
    Assert.That(sized, Is.Empty);
  }

  [Test]
  public void Of_AnOverriddenMembersHint_NamesItsParametersByPosition()
  {
    // Arrange (the base names the points pts, the override faceIds)
    var declarer = CellPoints("vtk::sizehint(pts, npts)");
    var m = declarer with
    {
      Parameters =
      [.. declarer.Parameters.Take(2), declarer.Parameters[2] with { Name = "faceIds" }],
      Hints = null,
    };

    // Act
    var sized = SizeHints.Of(m, declarer);

    // Assert
    Assert.That(sized, Is.EqualTo(new Dictionary<int, string?> { [2] = "(*arg3)" }));
  }
}
