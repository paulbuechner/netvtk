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

/// <summary>C++ signature types to netvtk-core's contract, or a skip reason.</summary>
[TestFixture]
public class SignatureMapperTests
{
  private SignatureMapper _mapper = null!;

  [SetUp]
  public void CreateMapper() => _mapper = new SignatureMapper(Registry());

  [Test]
  public void Parameter_ConstReferenceToValueType_StaysAReference()
  {
    // Arrange
    var type = Ref(Const(Class("vtkVector3d")));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const vtkVector3d&"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("vtkVector3d"));
      Assert.That(mapped.Type?.Uses,
                  Is.EqualTo(new[] { new TypeUse("CommonDataModel", "vtkVector3d.h") }));
    }
  }

  [Test]
  public void Parameter_MutableReferenceToValueType_IsRef()
  {
    // Arrange
    var type = Ref(Class("vtkVector3d"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Type?.CsKey, Is.EqualTo("ref vtkVector3d"));
  }

  [Test]
  public void Parameter_SmartPointerOfAVtkObject_MapsToItsProxy()
  {
    // Arrange
    var type = Ref(Const(SmartPointer("vtkTransform")));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const vtkSmartPointer<vtkTransform>&"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("vtkTransform"));
      Assert.That(mapped.Type?.Uses.Select(u => u.Package),
                  Is.EqualTo(new[] { "CommonTransforms" }));
    }
  }

  [Test]
  public void Parameter_SmartPointerOfUnwrappedClass_IsSkipped()
  {
    // Arrange
    var type = Ref(Const(SmartPointer("vtkInformation")));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type, Is.Null);
      Assert.That(mapped.Skip, Does.Contain("unwrapped"));
    }
  }

  [TestCase("const char*")]
  [TestCase("const char16_t*")]
  [TestCase("const std::string&")]
  public void Parameter_Strings_BecomeCSharpStrings(string spelling)
  {
    // Arrange
    CppType type = spelling switch
    {
      "const char*" => Pointer(Const(Builtin("char"))),
      "const char16_t*" => Pointer(Const(Builtin("char16_t"))),
      _ => Ref(Const(Template("std::basic_string", Builtin("char")))),
    };

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo(spelling));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("string"));
    }
  }

  [Test]
  public void Parameter_OStreamReference_IsAStream()
  {
    // Arrange
    var type = OStream;

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("std::ostream&"),
                  "the spelling Streams.i's typemaps match");
      Assert.That(mapped.Type?.CsType, Is.EqualTo("global::System.IO.Stream"));
    }
  }

  [Test]
  public void Return_TheStreamItWasGiven_IsVoid()
  {
    // Arrange (ostream& Print(ostream& os))
    ParameterModel[] parameters = [new("os", OStream, null)];

    // Act
    var chained = _mapper.Return(OStream, parameters);
    var alone = _mapper.Return(OStream, []);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(chained.Type?.Spelling, Is.EqualTo("void"));
      Assert.That(alone.Skip, Does.StartWith("returns a mutable reference"),
                  "a stream the object keeps");
    }
  }

  [TestCase("double", "ref double")]
  [TestCase("vtkVector3d", "ref vtkVector3d")]
  [TestCase("vtkVariant", "vtkVariant")]
  [TestCase("vtkSmartPointer<vtkTransform>", "vtkTransform")]
  [TestCase("vtkTimerLogScope", "void")]
  public void Return_MutableReferenceOfAMember_Maps(string referee, string csType)
  {
    // Arrange (a member of vtkTimerLogScope; its own class comes back for chaining)
    var type = Ref(referee switch
    {
      "double" => Builtin("double"),
      "vtkSmartPointer<vtkTransform>" => SmartPointer("vtkTransform"),
      _ => Class(referee),
    });

    // Act
    var member = _mapper.Return(type, [], owner: "vtkTimerLogScope");
    var alone = _mapper.Return(type, []);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(member.Type?.CsType, Is.EqualTo(csType),
                  "a C# ref, a borrowing proxy, the smart pointer's object, or void");
      Assert.That(alone.Skip, Does.StartWith("returns a mutable reference"),
                  "a static function has no object to borrow from");
    }
  }

  [Test]
  public void Parameter_TemplateInstanceNoClassNames_IsSkipped()
  {
    // Arrange (an instance the registry doesn't resolve to a class: no proxy)
    var type = Ref(Const(Template("vtkAOSDataArrayTemplate", Builtin("long long"))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Skip,
                Is.EqualTo("template vtkAOSDataArrayTemplate<long long> is not wrapped"));
  }

  [TestCase("wchar_t", "fixed-width")]
  public void Parameter_PlatformWidthBuiltin_IsSkipped(string builtin, string reason)
  {
    // Arrange
    var type = Builtin(builtin);

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Skip, Does.Contain(reason));
  }

  [Test]
  public void Parameter_CLong_IsACsLongKeyedAsLongLong()
  {
    // Arrange (32 bits on Windows, 64 elsewhere: a C# long, range-checked in Types.i; C# can't tell
    // it from long long)
    var type = Ref(Builtin("long"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("long&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("ref long"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("ref long long"));
    }
  }

  [Test]
  public void Parameter_Char_IsItsByte()
  {
    // Arrange (its sign differs per platform, and a C# char would pass through a code page)
    var type = Builtin("char");

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("char"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("byte"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("unsigned char"),
                  "C# can't tell it from unsigned char");
    }
  }

  [Test]
  public void Parameter_CodePoint_IsAUint()
  {
    // Arrange (char32_t: UTF-32, 32 bits everywhere)
    var type = Builtin("char32_t");

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsType, Is.EqualTo("uint"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("unsigned int"),
                  "C# can't tell it from unsigned int");
    }
  }

  [Test]
  public void Parameter_PointerSizedNumber_IsKeyedAsAnAddress()
  {
    // Arrange (C# can't overload F(void*) with F(intptr_t): both take an IntPtr)
    var number = Builtin("intptr_t");

    // Act
    var (mappedNumber, mappedAddress) = (_mapper.Parameter(number),
      _mapper.Parameter(Pointer(Builtin("void"))));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mappedNumber.Type?.CsType, Is.EqualTo("global::System.IntPtr"));
      Assert.That(mappedNumber.Type?.CsKey, Is.EqualTo(mappedAddress.Type?.CsKey));
    }
  }

  [Test]
  public void Return_SizeReferenceOfAMember_IsARefUIntPtr()
  {
    // Arrange (size_t is four bytes on win-x86)
    var type = Ref(Builtin("size_t"));

    // Act
    var mapped = _mapper.Return(type, [], "Demo_Stats");

    // Assert
    Assert.That(mapped.Type?.CsType, Is.EqualTo("ref global::System.UIntPtr"));
  }

  [Test]
  public void Parameter_WidthTypedefReference_KeepsItsName()
  {
    // Arrange (int64_t& is long& on Linux: Types.i applies INOUT to the written name)
    var type = Ref(new BuiltinType("long long", false, "int64_t"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("int64_t&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("ref long"));
    }
  }

  [Test]
  public void Parameter_StdStream_IsSkipped()
  {
    // Arrange
    var type = Ref(Class("std::basic_ostream"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Skip, Does.Contain("std type"));
  }

  [Test]
  public void Parameter_EnumReference_IsRefEnum()
  {
    // Arrange
    var type = Ref(Enum("vtkEventDataDevice"));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("vtkEventDataDevice&"));
      Assert.That(mapped.Type?.CsKey, Is.EqualTo("ref vtkEventDataDevice"));
    }
  }

  [Test]
  public void Return_ConstReferenceToValueClass_IsCopied()
  {
    // Arrange
    var type = Ref(Const(Class("vtkVariant")));

    // Act
    var mapped = _mapper.Return(type);

    // Assert
    Assert.That(mapped.Type?.Spelling, Is.EqualTo("const vtkVariant&"));
  }

  [Test]
  public void Return_ConstReferenceToPlainClass_IsSkipped()
  {
    // Arrange
    var type = Ref(Const(Class("vtkTimerLogScope")));

    // Act
    var mapped = _mapper.Return(type);

    // Assert
    Assert.That(mapped.Skip, Does.Contain("isn't copyable"));
  }

  [Test]
  public void Return_MutableReference_IsSkipped()
  {
    // Arrange
    var type = Ref(Class("vtkVariant"));

    // Act
    var mapped = _mapper.Return(type);

    // Assert
    Assert.That(mapped.Skip, Does.Contain("mutable reference"));
  }

  [TestCase("std::basic_string")]
  [TestCase("std::__cxx11::basic_string")]
  public void Parameter_StdString_IsAString(string template)
  {
    // Arrange (libstdc++ declares it in an inline namespace)
    var type = Ref(Const(Template(template, Builtin("char"),
                                  Template("std::char_traits", Builtin("char")),
                                  Template("std::allocator", Builtin("char")))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const std::string&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("string"));
    }
  }

  [Test]
  public void Parameter_StdArrayOfNumbers_IsACsArrayThroughItsMacro()
  {
    // Arrange
    var type = Ref(Const(Template("std::array", Builtin("double"), new ConstantArgument("3"))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const std::array< double, 3 >&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("double[]"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netvtk_std_array(%arg(double), 3, double)"));
    }
  }

  [TestCase("char")]
  [TestCase("bool")]
  [TestCase("long long")]
  public void Parameter_StdArrayOfAnElementCSharpDoesntPin_IsSkipped(string element)
  {
    // Arrange (char and bool marshal differently; template arguments are canonical, and int64_t is
    // long on Linux)
    var type = Ref(Const(Template("std::array", Builtin(element), new ConstantArgument("3"))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Type, Is.Null);
  }

  [Test]
  public void Parameter_ArrayReference_IsACsArrayThroughItsMacro()
  {
    // Arrange (int (&)[3]: three elements, which the callee reads and writes in place)
    var type = Ref(new ArrayType(Builtin("int"), 3));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("NetVtk_ArrayRef< int, 3 >"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("int[]"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netvtk_array_ref(int, 3, int)"));
    }
  }

  [Test]
  public void Parameter_BoolArrayReference_IsOneBytePerElement()
  {
    // Arrange
    var type = Ref(new ArrayType(Const(Builtin("bool")), 3));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netvtk_bool_array_ref(3)"));
  }

  [Test]
  public void Parameter_StdBitset_IsAMaskThroughItsMacro()
  {
    // Arrange
    var type = Ref(Const(Template("std::bitset", new ConstantArgument("18"))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const std::bitset< 18 >&"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("ulong"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netvtk_bitset(18)"));
    }
  }

  [Test]
  public void Return_StdOptionalOfAStruct_IsNullable()
  {
    // Arrange
    var type = Template("std::optional", Class("vtkVector3d"));

    // Act
    var mapped = _mapper.Return(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.CsType, Is.EqualTo("vtkVector3d?"));
      Assert.That(mapped.Type?.Typemap,
                  Is.EqualTo("%netvtk_optional(%arg(vtkVector3d), vtkVector3d)"));
    }
  }

  [Test]
  public void Parameter_StdSharedPointer_IsSkipped()
  {
    // Arrange
    var type =
      Ref(Const(Template("std::shared_ptr", Template("std::basic_istream", Builtin("char")))));

    // Act
    var mapped = _mapper.Parameter(type);

    // Assert
    Assert.That(mapped.Skip, Is.EqualTo("std type std::shared_ptr"));
  }

  [Test]
  public void Field_WidthTypedef_IsSpelledByItsName()
  {
    // Arrange (vtkIdType: VtkObjects.i's typemaps match the name)
    var type = new BuiltinType("long long", Written: "vtkIdType");

    // Act
    var mapped = _mapper.Field(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("vtkIdType"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("long"));
    }
  }

  [Test]
  public void Field_VtkObject_IsAConstPointer()
  {
    // Arrange
    var type = Pointer(Class("vtkTransform"));

    // Act
    var mapped = _mapper.Field(type);

    // Assert (read only, its getter adds a reference for the proxy: VtkObjects.i)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("vtkTransform* const"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("vtkTransform"));
      Assert.That(mapped.Type?.Uses.Select(u => u.Package),
                  Is.EqualTo(new[] { "CommonTransforms" }));
    }
  }

  [Test]
  public void Field_PointerToAValueClass_IsAProxyThatBorrows()
  {
    // Arrange
    var type = Pointer(Class("vtkVariant"));

    // Act
    var mapped = _mapper.Field(type);

    // Assert (read only: References.i's getter keeps the owner's proxy alive)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("vtkVariant* const"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("vtkVariant"));
    }
  }

  [TestCase("void", false, "void*")]
  [TestCase("void", true, "const void*")]
  [TestCase("int", false, "void* const")]
  [TestCase("vtkVector3d", false, "void* const")]
  public void Field_OtherPointer_IsAnAddress(string pointee, bool isConst, string spelling)
  {
    // Arrange (a void* read and written; any other pointer read only)
    CppType target = pointee == "vtkVector3d" ? Class(pointee) : Builtin(pointee);
    var type = Pointer(isConst ? Const(target) : target);

    // Act
    var mapped = _mapper.Field(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo(spelling));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("global::System.IntPtr"));
    }
  }

  [TestCase(false)]
  [TestCase(true)]
  public void Field_CString_IsAReadOnlyString(bool isConst)
  {
    // Arrange (C# can't tell who owns the text)
    var type = Pointer(isConst ? Const(Builtin("char")) : Builtin("char"));

    // Act
    var mapped = _mapper.Field(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const char* const"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("string"));
    }
  }

  [Test]
  public void Field_CArrayOfNumbers_IsACSharpArray()
  {
    // Arrange
    var type = new ArrayType(new BuiltinType("long long", Written: "vtkIdType"), 3);

    // Act
    var mapped = _mapper.Field(type);

    // Assert (declared as C++ has it, which Types.i's typemaps copy both ways)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Declare("Ids"), Is.EqualTo("vtkIdType Ids[3]"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("long[]"));
    }
  }

  [TestCase("bool")]
  [TestCase("char")]
  [TestCase("size_t")]
  public void Field_CArrayOfOtherElements_IsSkipped(string element)
  {
    // Arrange (bool and char arrays marshal through other typemaps; size_t has no fixed width)
    var type = new ArrayType(Builtin(element), 4);

    // Act
    var mapped = _mapper.Field(type);

    // Assert
    Assert.That(mapped.Skip, Does.StartWith($"a field of {element}[4]: "));
  }

  [TestCase("vtkVector3d", "CommonDataModel")]
  [TestCase("vtkVariant", "CommonCore")]
  public void Field_StructOrValueClass_IsACopy(string name, string package)
  {
    // Arrange
    var type = Class(name);

    // Act
    var mapped = _mapper.Field(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo(name));
      Assert.That(mapped.Type?.CsType, Is.EqualTo(name));
      Assert.That(mapped.Type?.Uses.Select(u => u.Package), Is.EqualTo(new[] { package }));
    }
  }

  [Test]
  public void Field_PlainClass_IsSkipped()
  {
    // Arrange (no copy for the property)
    var type = Class("vtkTimerLogScope");

    // Act
    var mapped = _mapper.Field(type);

    // Assert
    Assert.That(mapped.Type, Is.Null);
  }

  [Test]
  public void Field_SmartPointer_IsTheObject()
  {
    // Arrange
    var type = SmartPointer("vtkTransform");

    // Act
    var mapped = _mapper.Field(type);

    // Assert (read and written: the field counts a reference)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("vtkSmartPointer<vtkTransform>"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("vtkTransform"));
    }
  }

  [Test]
  public void Field_String_IsAString()
  {
    // Arrange (std::string as libclang spells it)
    var type = Template("std::basic_string", Builtin("char"),
                        Template("std::char_traits", Builtin("char")),
                        Template("std::allocator", Builtin("char")));

    // Act
    var mapped = _mapper.Field(type);

    // Assert (Strings.i's typemaps, which a property reads and writes through)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("std::string"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("string"));
    }
  }

  [Test]
  public void Field_VectorOfAValueClass_IsAVectorOfCopies()
  {
    // Arrange
    var type = Template("std::vector", Class("vtkVariant"),
                        Template("std::allocator", "vtkVariant"));

    // Act
    var mapped = _mapper.Field(type);

    // Assert (read and written, a C# array of copies through Std.i's macro)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("std::vector< vtkVariant >"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("vtkVariant[]"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netvtk_valueclass_vector(vtkVariant)"));
      Assert.That(mapped.Type?.Uses.Select(u => u.Package), Is.EqualTo(new[] { "CommonCore" }));
    }
  }

  [Test]
  public void Field_VectorOfVtkObjectPointers_IsReadOnly()
  {
    // Arrange (nothing would count the references of objects a property put there)
    var type = Template("std::vector", Pointer(Class("vtkTransform")),
                        Template("std::allocator", Pointer(Class("vtkTransform"))));

    // Act
    var mapped = _mapper.Field(type);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(mapped.Type?.Spelling, Is.EqualTo("const std::vector< vtkTransform* >"));
      Assert.That(mapped.Type?.CsType, Is.EqualTo("vtkTransform[]"));
      Assert.That(mapped.Type?.Typemap, Is.EqualTo("%netvtk_object_vector(vtkTransform)"));
    }
  }

  [Test]
  public void Field_UnorderedMap_IsSkipped()
  {
    // Arrange
    var type = Template("std::unordered_map", Builtin("int"), Builtin("int"));

    // Act
    var mapped = _mapper.Field(type);

    // Assert
    Assert.That(mapped.Skip, Does.StartWith("a field of std::unordered_map: "));
  }
}
