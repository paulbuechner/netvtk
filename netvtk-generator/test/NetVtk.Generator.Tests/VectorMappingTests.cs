// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Linq;

// NUnit
using NUnit.Framework;

//
using NetVtk.Generator.Mapping;
using NetVtk.Generator.Model;

// static usings
using static NetVtk.Generator.Tests.Types;

namespace NetVtk.Generator.Tests;

/// <summary>
/// <c>std::vector</c> to netvtk-core's contract: a C# array of numbers, strings, structs, value
/// classes or VTK objects, copied both ways through its elements' macro; or a skip reason.
/// </summary>
[TestFixture]
public class VectorMappingTests
{
  private SignatureMapper _mapper = null!;

  [SetUp]
  public void CreateMapper() => _mapper = new SignatureMapper(Registry());

  // std::vector<T> as libclang spells it
  private static NamedType Vector(CppType element) =>
    Template("std::vector", element, Template("std::allocator", element));

  private static NamedType String => Template("std::basic_string", Builtin("char"),
                                              Template("std::char_traits", Builtin("char")),
                                              Template("std::allocator", Builtin("char")));

  [Test]
  public void Parameter_ConstReferenceToNumbers_IsAnArray()
  {
    // Arrange
    var type = Ref(Const(Vector(Builtin("double"))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const std::vector< double >&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("double[]"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netvtk_vector(double, double)"));
    }
  }

  [Test]
  public void Parameter_WidthTypedefs_KeepTheirName()
  {
    // Arrange (vtkIdType, as the parser keeps a standard library template's arguments as written)
    var type = Vector(new BuiltinType("long long", Written: "vtkIdType"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("std::vector< vtkIdType >"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("long[]"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netvtk_vector(vtkIdType, long)"));
    }
  }

  [Test]
  public void Parameter_MutableReference_IsARefOfTheArray()
  {
    // Arrange (the callee writes the caller's vector: its elements come back)
    var type = Ref(Vector(String));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert (Std.i has the strings' typemaps: no macro)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("std::vector< std::string >&"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("ref string[]"));
      Assert.That(mapped.Type?.Typemap, Is.Null);
    }
  }

  [Test]
  public void Parameter_Pointer_IsARefOfTheArray()
  {
    // Arrange (null passes nullptr)
    var type = Pointer(Vector(Builtin("unsigned char")));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("std::vector< unsigned char >*"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("ref byte[]"));
    }
  }

  [TestCase(false, "std::vector< vtkTransform* >")]
  [TestCase(true, "std::vector< vtkSmartPointer< vtkTransform > >")]
  public void Parameter_VtkObjects_AreAnArrayOfTheirProxies(bool handles, string spelling)
  {
    // Arrange
    CppType element = handles ? SmartPointer("vtkTransform") : Pointer(Class("vtkTransform"));

    // Act
    var mapped = _mapper.Parameter(Vector(element));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo(spelling));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("vtkTransform[]"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netvtk_object_vector(vtkTransform)"));
      Assert.That(mapped.Type?.Uses.Select(u => u.Package),
                  Is.EqualTo(new[] { "CommonTransforms" }));
    }
  }

  [TestCase("vtkVector3d", "%netvtk_vector(vtkVector3d, vtkVector3d)")]
  [TestCase("vtkVariant", "%netvtk_valueclass_vector(vtkVariant)")]
  public void Parameter_StructsAndValueClasses_AreAnArrayOfCopies(string name, string typemap)
  {
    // Arrange
    var type = Ref(Const(Vector(Class(name))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsType, Is.EqualTo($"{name}[]"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo(typemap));
    }
  }

  [Test]
  public void Parameter_SizeT_IsSkipped()
  {
    // Arrange (its width differs per platform)
    var type = Vector(Builtin("size_t"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Skip,
                Is.EqualTo(
                  "a vector of size_t: C# arrays of vectors hold numbers, strings, structs, value classes and VTK objects"));
  }

  [TestCase("vtkTimerLogScope")]
  [TestCase("vector")]
  public void Parameter_OtherElements_AreSkipped(string element)
  {
    // Arrange (a plain class has no copies, a vector of vectors no C# array)
    CppType type = element == "vector" ? Vector(Vector(Builtin("int"))) : Vector(Class(element));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Skip, Does.StartWith("a vector of "));
  }

  [Test]
  public void Return_ByValue_IsAnArray()
  {
    // Arrange
    var type = Vector(Builtin("unsigned int"));

    // Act
    var mapped = _mapper.Return(type, [], owner: "vtkTimerLogScope");

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("std::vector< unsigned int >"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("uint[]"));
    }
  }

  [TestCase(true, "std::vector< float >&")]
  [TestCase(false, "std::vector< float >*")]
  public void Return_MutableReferenceOrPointer_IsACopy(bool reference, string spelling)
  {
    // Arrange (C# has no array that writes through)
    var vector = Vector(Builtin("float"));
    CppType type = reference ? Ref(vector) : Pointer(vector);

    // Act
    var mapped = _mapper.Return(type, [], owner: "vtkTimerLogScope");

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo(spelling));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("float[]"));
    }
  }
}
