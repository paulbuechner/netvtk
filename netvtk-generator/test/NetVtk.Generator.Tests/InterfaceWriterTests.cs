// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using NetVtk.Generator.Config;
using NetVtk.Generator.Emit;
using NetVtk.Generator.Mapping;
using NetVtk.Generator.Model;

// static usings
using static NetVtk.Generator.Tests.Types;

namespace NetVtk.Generator.Tests;

/// <summary>Declarations the writer emits for a synthetic package "Demo".</summary>
[TestFixture]
public class InterfaceWriterTests
{
  private TypeRegistry _registry = null!;

  [SetUp]
  public void CreateRegistry() => _registry = Registry();

  [Test]
  public void Write_GivesEnumsExplicitValues()
  {
    // Arrange
    var package = Package(enums:
    [
      new EnumModel("Demo_Kind", "Demo_Kind.h",
                    [new EnumConstant("Demo_A", 1), new EnumConstant("Demo_B", 4)])
    ]);

    // Act
    var module = Write(package);

    // Assert
    Assert.That(module.Interface,
                Does.Contain("enum Demo_Kind {\n  Demo_A = 1,\n  Demo_B = 4,\n};"));
  }

  [Test]
  public void Write_DeclaresObjectTypemapsBeforeAnyClass()
  {
    // Arrange (Demo_Owner returns a smart pointer to Demo_Part, which comes later in the package)
    var owner = new ClassModel("Demo_Owner", "Demo_Owner.h", ["vtkObjectBase"], TransientTraits, [],
                               [Method("Part", SmartPointer("Demo_Part"))]);
    var part = new ClassModel("Demo_Part", "Demo_Part.h", ["vtkObjectBase"], TransientTraits, [],
                              []);

    // Act
    var module = Write(Package(classes: [owner, part]));

    // Assert
    var text = module.Interface;
    using (Assert.EnterMultipleScope())
    {
      Assert.That(text.IndexOf("%vtk_object(Demo_Part)", StringComparison.Ordinal),
                  Is.LessThan(text.IndexOf("class Demo_Owner", StringComparison.Ordinal)));
      Assert.That(text, Does.Contain("vtkSmartPointer<Demo_Part> Part();"));
    }
  }

  [Test]
  public void Write_DerivesFromTheNearestWrappedAncestor()
  {
    // Arrange (neither vtkPolyDataAlgorithm nor vtkAlgorithm is wrapped here; the proxy must still
    // be a VTK object)
    var tool = new ClassModel("Demo_Tool", "Demo_Tool.h",
                              ["vtkPolyDataAlgorithm", "vtkAlgorithm", "vtkObjectBase"],
                              TransientTraits, [], []);

    // Act
    var module = Write(Package(classes: [tool]));

    // Assert
    Assert.That(module.Interface, Does.Contain("class Demo_Tool : public vtkObjectBase {"));
  }

  [Test]
  public void Write_KeepsTrailingClassDefaults()
  {
    // Arrange (a value class's default: C# calls without it get C++'s)
    var perform = Method("Perform", Builtin("void"),
                         new ParameterModel("thePoint", Ref(Const(Class("vtkVector3d"))), null),
                         new ParameterModel("theValue", Ref(Const(Class("vtkVariant"))),
                                            "vtkVariant()"));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Algo", perform)]));

    // Assert
    Assert.That(module.Interface,
                Does.Contain(
                  "  void Perform(const vtkVector3d& thePoint, const vtkVariant& theValue = vtkVariant());"));
  }

  [Test]
  public void Write_CoversOverloadsThatCollideInCSharp()
  {
    // Arrange (const char* and std::string are both UTF-8 strings in C#: one call, which reaches
    // the first)
    var byPointer = Method("Load", Builtin("bool"),
                           new ParameterModel("theName", Pointer(Const(Builtin("char"))), null));
    var byString = Method("Load", Builtin("bool"),
                          new ParameterModel("theName",
                                             Ref(Const(Template("std::basic_string",
                                                                Builtin("char")))), null));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Store", byPointer, byString)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("bool Load(const char* theName);"));
      Assert.That(module.Interface, Does.Not.Contain("std::string& theName"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_PrefersTheNonConstTwin()
  {
    // Arrange (its ref return reads as well as writes)
    var read = Method("Value", Ref(Const(Builtin("double")))) with { IsConst = true };
    var write = Method("Value", Ref(Builtin("double")));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Cell", read, write)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  double& Value();"));
      Assert.That(module.Interface, Does.Not.Contain("Value() const;"));
    }
  }

  [Test]
  public void Write_KeepsTheFirstTwinReturningAClass()
  {
    // Arrange (neither an owned copy nor a borrowed proxy is safe for every class: declaration
    // order decides)
    var write = Method("Value", Ref(Class("vtkVariant")));
    var read = Method("Value", Ref(Const(Class("vtkVariant")))) with { IsConst = true };

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Iterator", write, read)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  vtkVariant& Value();"));
      Assert.That(module.Interface, Does.Not.Contain("Value() const;"));
    }
  }

  [Test]
  public void Write_PrefersTheUtf16StringOverload()
  {
    // Arrange (a C# string reaches VTK exactly as UTF-16; a const char* overload may copy its
    // UTF-8 bytes as Latin-1)
    var narrow = Method("Load", Builtin("bool"),
                        new ParameterModel("theName", Pointer(Const(Builtin("char"))), null));
    var wide = Method("Load", Builtin("bool"),
                      new ParameterModel("theName", Pointer(Const(Builtin("char16_t"))), null));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Store", narrow, wide)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("bool Load(const char16_t* theName);"));
      Assert.That(module.Interface, Does.Not.Contain("const char* theName"));
    }
  }

  [Test]
  public void Write_KeepsTheDefaultsPastAClaimedOverload()
  {
    // Arrange (Send(string) is the UTF-16 overload's; the other keeps its two-argument call)
    var wide = Method("Send", Builtin("void"),
                      new ParameterModel("theText", Pointer(Const(Builtin("char16_t"))), null));
    var narrow = Method("Send", Builtin("void"),
                        new ParameterModel("theText", Pointer(Const(Builtin("char"))), null),
                        new ParameterModel("theLevel", Builtin("int"), "0"));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Log", narrow, wide)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("  void Send(const char* theText, int theLevel);"));
      Assert.That(module.Interface, Does.Contain("  void Send(const char16_t* theText);"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_LeavesOutABaseItDoesntWrap()
  {
    // Arrange (std::exception has no proxy: the root of the hierarchy declares no base)
    var root = new ClassModel("Demo_Error", "Demo_Error.h", ["std::exception"], ValueTraits, [],
                              []);
    var failure = new ClassModel("Demo_Failure", "Demo_Failure.h", ["Demo_Error", "std::exception"],
                                 ValueTraits, [], []);

    // Act
    var module = Write(Package(classes: [root, failure]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("class Demo_Error {"));
      Assert.That(module.Interface, Does.Contain("class Demo_Failure : public Demo_Error {"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_ImportsWhatItUsesWithTheirDependencies()
  {
    // Arrange
    var move = Method("Move", Builtin("void"),
                      new ParameterModel("theTarget", Ref(Const(Class("vtkVector3d"))), null));
    Dictionary<string, IReadOnlyList<string>> imports = new()
      { ["CommonDataModel"] = ["CommonCore"] };

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Mover", move)]), imports);

    // Assert
    Assert.That(module.Imports, Is.EqualTo(new[] { "CommonCore", "CommonDataModel" }));
  }

  [Test]
  public void Write_ImportsThePackagesItsDependenciesDeriveFromFirst()
  {
    // Arrange (Left and Right use each other, and Right_Shape derives from Left_Shape: SWIG must
    // meet Left first)
    _registry.AddClass(new KnownClass("Left_Shape", "Left", WrapKind.ValueClass));
    _registry.AddClass(new KnownClass("Right_Shape", "Right", WrapKind.ValueClass));
    var fit = Method("Fit", Builtin("void"),
                     new ParameterModel("theRight", Ref(Const(Class("Right_Shape"))), null),
                     new ParameterModel("theLeft", Ref(Const(Class("Left_Shape"))), null));
    Dictionary<string, IReadOnlyList<string>> imports = new()
      { ["Left"] = ["Right"], ["Right"] = ["Left"] };
    Dictionary<string, IReadOnlyCollection<string>> bases = new() { ["Right"] = ["Left"] };

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Fitter", fit)]), imports, bases);

    // Assert (without the bases, the cycle puts Right first)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Imports, Is.EqualTo(new[] { "Left", "Right" }));
      Assert.That(module.Interface,
                  Does.Contain(
                    "#ifndef SWIGIMPORTED\n%import \"Left.i\"\n%import \"Right.i\"\n#endif\n"),
                  "a module SWIG imports doesn't import again");
    }
  }

  [Test]
  public void Write_RecordsThePackagesItsClassesDeriveFrom()
  {
    // Arrange
    var maker = new ClassModel("Demo_Maker", "Demo_Maker.h", ["vtkTimerLogScope"], ValueTraits, [],
                               []);

    // Act
    var module = Write(Package(classes: [maker]));

    // Assert
    Assert.That(module.BasePackages, Is.EqualTo(new[] { "CommonSystem" }));
  }

  [Test]
  public void Write_IncludesDependencyHeadersBeforeThePackagesOwn()
  {
    // Arrange
    var move = Method("Move", Builtin("void"),
                      new ParameterModel("theTarget", Ref(Const(Class("vtkVector3d"))), null));

    // Act
    var header = Write(Package(classes: [ValueClass("Demo_Mover", move)])).ModuleHeader;

    // Assert
    Assert.That(header.IndexOf("#include <vtkVector3d.h>", StringComparison.Ordinal),
                Is.Not.Negative.And.LessThan(
                  header.IndexOf("#include <Demo.h>", StringComparison.Ordinal)),
                "headers that use a type without including it still compile");
  }

  [Test]
  public void Write_LendsStreamsForTheCallOnly()
  {
    // Arrange (a constructor, or a class that holds a stream, may keep the stream)
    ParameterModel stream = new("theStream", OStream, null);
    var dumper = new ClassModel("Demo_Dumper", "Demo_Dumper.h", [], ValueTraits,
                                [new ConstructorModel([stream], false)],
                                [Method("Dump", Builtin("void"), stream)]);
    var printer = new ClassModel("Demo_Printer", "Demo_Printer.h", [], ValueTraits, [],
                                 [Method("Print", Builtin("void"), stream)],
                                 Held: [Pointer(OStream.Referee)]);

    // Act
    var module = Write(Package(classes: [dumper, printer]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  void Dump(std::ostream& theStream);"));
      Assert.That(module.Skipped,
                  Does.Contain(
                    "Demo_Dumper::Demo_Dumper: parameter theStream: a constructor may keep the stream, which C# lends for the call only"));
      Assert.That(module.Skipped,
                  Does.Contain(
                    "Demo_Printer::Print: parameter theStream: the class may keep the stream, which C# lends for the call only"));
    }
  }

  [Test]
  public void Write_OrdersImportsAndIncludesWhateverTheMembersOrder()
  {
    // Arrange (two members using the types of two other packages, declared in either order)
    var move = Method("Move", Builtin("void"),
                      new ParameterModel("theTarget", Ref(Const(Class("vtkVector3d"))), null));
    var apply = Method("Apply", Builtin("void"),
                       new ParameterModel("theTransform", Ref(Const(SmartPointer("vtkTransform"))),
                                          null));
    Dictionary<string, IReadOnlyList<string>> imports = new()
      { ["CommonDataModel"] = ["CommonCore"], ["CommonTransforms"] = ["CommonCore"] };

    // Act
    var first = Write(Package(classes: [ValueClass("Demo_Mover", move, apply)]), imports);
    var second = Write(Package(classes: [ValueClass("Demo_Mover", apply, move)]), imports);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(first.Imports,
                  Is.EqualTo(new[] { "CommonCore", "CommonDataModel", "CommonTransforms" }));
      Assert.That(second.Imports, Is.EqualTo(first.Imports));
      Assert.That(second.ModuleHeader, Is.EqualTo(first.ModuleHeader));
    }
  }

  [Test]
  public void Write_PointersTheCalleeMayKeep_AreAddressesTypemappedBeforeTheClasses()
  {
    // Arrange (a constructor or setter may keep the pointer; a C# array is pinned for the call
    // only)
    var values = new ParameterModel("theValues", Pointer(Const(Builtin("double"))), null);
    var buffer = new ClassModel("Demo_Buffer", "Demo_Buffer.h", [], ValueTraits,
                                [new ConstructorModel([values], false)], [
                                  Method("SetValues", Builtin("void"), values),
                                  Method("Sum", Builtin("double"), values)
                                ]);

    // Act
    var text = Write(Package(classes: [buffer])).Interface;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(text, Does.Contain("  Demo_Buffer(NetVtk_Address< const double* > theValues);"));
      Assert.That(
        text, Does.Contain("  void SetValues(NetVtk_Address< const double* > theValues);"));
      Assert.That(text, Does.Contain("  double Sum(const double* theValues);"));
      Assert.That(text.IndexOf("%netvtk_address(%arg(const double*))", StringComparison.Ordinal),
                  Is.LessThan(text.IndexOf("class Demo_Buffer", StringComparison.Ordinal)));
    }
  }

  [Test]
  public void Write_MoveOverloadWithACopyTwin_IsCoveredNotSkipped()
  {
    // Arrange (C# has no moves: its call reaches the const& overload)
    var byCopy = Method("SetValue", Builtin("void"),
                        new ParameterModel("theValue", Ref(Const(Class("vtkVariant"))), null));
    var byMove = Method("SetValue", Builtin("void"),
                        new ParameterModel("theValue",
                                           new ReferenceType(Class("vtkVariant"), IsRValue: true),
                                           null));

    // Act
    var module = Write(Package(classes: [ValueClass("Demo_Holder", byCopy, byMove)]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  void SetValue(const vtkVariant& theValue);"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_MarksADeprecatedOverloadObsolete()
  {
    // Arrange (SWIG matches the feature by name, parameters, defaults and const: the other overload
    // stays unmarked)
    var shape = ValueClass("Demo_Shape",
                           Method("Reversed", Class("Demo_Shape")) with { IsConst = true }, Method(
                               "Reversed", Builtin("void"),
                               new ParameterModel("theResult", Ref(Class("Demo_Shape")), null),
                               new ParameterModel("theDeep", Builtin("bool"), "false")) with
                             {
                               IsConst = true, IsDeprecated = true
                             });

    // Act
    var module = Write(Package(classes: [shape]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("  Demo_Shape Reversed() const;\n"
                               + "  %csattributes Reversed(Demo_Shape& theResult, bool theDeep = false) const \"[global::System.Obsolete(\\\"Deprecated in VTK.\\\")]\";\n"
                               + "  void Reversed(Demo_Shape& theResult, bool theDeep = false) const;"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_MarksADeprecatedClassObsolete()
  {
    // Arrange (the class typemap: %csattributes on the class name would mark its constructor)
    var old = ValueClass("Demo_Old") with { IsDeprecated = true };

    // Act
    var module = Write(Package(classes: [old]));

    // Assert
    Assert.That(module.Interface,
                Does.Contain(
                  "%typemap(csattributes) Demo_Old \"[global::System.Obsolete(\\\"Deprecated in VTK.\\\")]\""));
  }

  [Test]
  public void Write_KeepsGetType_LeavesOutDispose()
  {
    // Arrange (VTK's GetType, vtkVariant's, hides object's, which isn't virtual; Dispose is the
    // proxy's own)
    var variant = ValueClass("Demo_Variant",
                             Method("GetType", Builtin("unsigned int")) with { IsConst = true },
                             Method("Dispose", Builtin("void")));

    // Act
    var module = Write(Package(classes: [variant]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  unsigned int GetType() const;"));
      Assert.That(module.Skipped,
                  Is.EqualTo(new[] { "Demo_Variant::Dispose: name clashes with the C# proxy" }));
    }
  }

  [Test]
  public void Write_KeepsTheCallsOnlyOneOverloadTakes()
  {
    // Arrange (one argument fits both, as in C++: each keeps the calls it alone takes, (), (int,
    // int))
    var byIncrement =
      new ConstructorModel([new ParameterModel("theIncrement", Builtin("int"), "256")],
                           IsDeprecated: false);
    var byLength = new ConstructorModel([
      new ParameterModel("theLength", Builtin("int"), null),
      new ParameterModel("theIncrement", Builtin("int"), "256")
    ], IsDeprecated: false);
    var array = new ClassModel("Demo_Array", "Demo_Array.h", [], ValueTraits,
                               [byIncrement, byLength], []);

    // Act
    var module = Write(Package(classes: [array]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("  Demo_Array();\n  Demo_Array(int theLength, int theIncrement);"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_ReturnsAStaticMembersClassReferenceThroughAnAccessor()
  {
    // Arrange (SWIG can't return the reference: the accessor returns its address, a proxy that
    // borrows the object and keeps no owner)
    var registry = new ClassModel("Demo_Registry", "Demo_Registry.h", [],
                                  ValueTraits with { IsCopyable = false }, [], [
                                    Method("Instance", Ref(Class("Demo_Registry"))) with
                                    {
                                      IsStatic = true
                                    }
                                  ]);

    // Act
    var module = Write(Package(classes: [registry]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("  %rename(Instance) NetVtk_Instance;\n  %extend {\n"
                               + "    static Demo_Registry* const NetVtk_Instance() { return &Demo_Registry::Instance(); }\n  }"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_KeepsTheArgumentsAConstructorRefersTo()
  {
    // Arrange (Demo_Iterator holds a reference to its value; the point is a struct, a copy for the
    // call)
    var iterator = new ClassModel("Demo_Iterator", "Demo_Iterator.h", [], ValueTraits, [
                                    new ConstructorModel([
                                      new ParameterModel("theValue",
                                                         Ref(Const(Class("vtkVariant"))), null),
                                      new ParameterModel("thePoint",
                                                         Ref(Const(Class("vtkVector3d"))), null)
                                    ], IsDeprecated: false)
                                  ], [], Held:
                                  [
                                    Ref(Const(Class("vtkVariant"))),
                                    Pointer(Const(Class("vtkVector3d")))
                                  ]);

    // Act
    var module = Write(Package(classes: [iterator]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("%netvtk_keep_construct(Demo_Iterator)"));
      Assert.That(module.Interface,
                  Does.Contain("  %apply SWIGTYPE & NETVTK_KEEP { const vtkVariant& theValue };\n"
                               + "  Demo_Iterator(const vtkVariant& theValue, const vtkVector3d& thePoint);\n  %clear const vtkVariant& theValue;"));
    }
  }

  [Test]
  public void Write_KeepsTheArgumentsAMemberRefersTo()
  {
    // Arrange (Initialize stores the value; a const member can't, and a struct argument is a copy
    // for the call)
    var locator = ValueClass("Demo_Locator",
                             Method("Initialize", Builtin("void"),
                                    new ParameterModel("theValue", Ref(Const(Class("vtkVariant"))),
                                                       null),
                                    new ParameterModel("thePoint", Ref(Const(Class("vtkVector3d"))),
                                                       null)), Method(
                                 "Contains", Builtin("bool"),
                                 new ParameterModel("theValue", Ref(Const(Class("vtkVariant"))),
                                                    null)) with
                               {
                                 IsConst = true
                               }) with
    {
      Held = [Pointer(Const(Class("vtkVariant")))]
    };

    // Act
    var module = Write(Package(classes: [locator]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("  %netvtk_keep_argument(Initialize.0, const vtkVariant& theValue)\n"
                               + "  void Initialize(const vtkVariant& theValue, const vtkVector3d& thePoint);\n  %clear const vtkVariant& theValue;"));
      Assert.That(module.Interface, Does.Not.Contain("%netvtk_keep_argument(Contains"));
      Assert.That(module.Interface, Does.Not.Contain("%netvtk_keep_construct"));
    }
  }

  [Test]
  public void Write_KeepsTheObjectAViewReturnRefersTo()
  {
    // Arrange (Demo_View refers to its graph, and so does Demo_Range, whose destructor may use it;
    // Demo_Label refers to nothing; a static member has no object to keep)
    var view = new ClassModel("Demo_View", "Demo_View.h", [], ValueTraits, [], [],
                              Held: [Pointer(Class("Demo_Graph"))]);
    var range = new ClassModel("Demo_Range", "Demo_Range.h", [],
                               ValueTraits with { DeclaresDestructor = true }, [], [],
                               Held: [Pointer(Class("Demo_Graph"))]);
    var label = new ClassModel("Demo_Label", "Demo_Label.h", [], ValueTraits, [], []);
    var graph = ValueClass("Demo_Graph",
                           Method("View", Ref(Const(Class("Demo_View")))) with { IsConst = true },
                           Method("Copy", Class("Demo_View")) with { IsConst = true },
                           Method("Next", Class("Demo_Range")),
                           Method("Label", Ref(Const(Class("Demo_Label")))) with { IsConst = true },
                           Method("Empty", Class("Demo_View")) with { IsStatic = true });

    // Act
    var module = Write(Package(classes: [view, range, label, graph]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("  %apply const SWIGTYPE & NETVTK_VIEW { const Demo_View& View };\n"
                               + "  const Demo_View& View() const;\n  %clear const Demo_View& View;"));
      Assert.That(module.Interface,
                  Does.Contain("  %apply SWIGTYPE NETVTK_VIEW { Demo_View Copy };\n"
                               + "  Demo_View Copy() const;\n  %clear Demo_View Copy;"));
      Assert.That(module.Interface,
                  Does.Contain("  %apply SWIGTYPE NETVTK_VIEW_HELD { Demo_Range Next };\n"
                               + "  Demo_Range Next();\n  %clear Demo_Range Next;"));
      Assert.That(module.Interface, Does.Not.Contain("Demo_Label& Label }"));
      Assert.That(module.Interface, Does.Not.Contain("Demo_View Empty }"));
    }
  }

  [Test]
  public void Write_KeepsTheObjectAMovedResultRefersTo()
  {
    // Arrange (Demo_Guard, move-only, refers to its graph and releases it in its destructor; its
    // const& return is a borrowing proxy, which keeps the object already)
    var guard = new ClassModel("Demo_Guard", "Demo_Guard.h", [], ValueTraits with
    {
      IsCopyable = false, IsMovable = true, DeclaresDestructor = true
    }, [], [], Held: [Pointer(Class("Demo_Graph"))]);
    var graph = ValueClass("Demo_Graph", Method("Edit", Class("Demo_Guard")),
                           Method("Current", Ref(Const(Class("Demo_Guard")))) with
                           {
                             IsConst = true
                           });

    // Act
    var module = Write(Package(classes: [guard, graph]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain(
                    "  %apply SWIGTYPE NETVTK_VIEW_HELD { Demo_Guard Edit };\n  Demo_Guard Edit();\n  %clear Demo_Guard Edit;"));
      Assert.That(module.Interface, Does.Contain("  const Demo_Guard& Current() const;"));
      Assert.That(module.Interface, Does.Not.Contain("Demo_Guard& Current }"));
    }
  }

  [Test]
  public void Write_KeepsNoStandardLibraryArgument()
  {
    // Arrange (a std::string is a C# string, which has no proxy to keep)
    var name = Template("std::basic_string", Builtin("char"));
    var label = new ClassModel("Demo_Label", "Demo_Label.h", [], ValueTraits, [
      new ConstructorModel([new ParameterModel("theName", Ref(Const(name)), null)],
                           IsDeprecated: false)
    ], [], Held: [Ref(Const(name))]);

    // Act
    var module = Write(Package(classes: [label]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  Demo_Label(const std::string& theName);"));
      Assert.That(module.Interface, Does.Not.Contain("NETVTK_KEEP"));
    }
  }

  [Test]
  public void Write_LeavesOutAMemberAStaticOverloadShadows()
  {
    // Arrange (SWIG passes the object as the first argument: Multiply(m) collides with static
    // Multiply(a, b))
    var matrix = ValueClass("Demo_Mat",
                            Method("Multiply", Builtin("void"),
                                   new ParameterModel("theOther", Ref(Const(Class("Demo_Mat"))),
                                                      null)), Method(
                                "Multiply", Class("Demo_Mat"),
                                new ParameterModel("theA", Ref(Const(Class("Demo_Mat"))), null),
                                new ParameterModel("theB", Ref(Const(Class("Demo_Mat"))), null))
                              with
                              {
                                IsStatic = true
                              });

    // Act
    var module = Write(Package(classes: [matrix]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain(
                    "static Demo_Mat Multiply(const Demo_Mat& theA, const Demo_Mat& theB);"));
      Assert.That(module.Interface, Does.Not.Contain("void Multiply("));
      Assert.That(module.Skipped,
                  Has.One.StartsWith("Demo_Mat::Multiply: a static overload takes the object"));
    }
  }

  [Test]
  public void Write_ComparesStaticOverloadsWithoutConstAndReferences()
  {
    // Arrange (by value and by const& are one type to SWIG: the wrappers still collide)
    var matrix = ValueClass("Demo_Mat",
                            Method("Multiply", Builtin("void"),
                                   new ParameterModel("theOther", Class("Demo_Mat"), null)), Method(
                                "Multiply", Class("Demo_Mat"),
                                new ParameterModel("theA", Ref(Const(Class("Demo_Mat"))), null),
                                new ParameterModel("theB", Ref(Const(Class("Demo_Mat"))), null))
                              with
                              {
                                IsStatic = true
                              });

    // Act
    var module = Write(Package(classes: [matrix]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Not.Contain("void Multiply("));
      Assert.That(module.Skipped,
                  Has.One.StartsWith("Demo_Mat::Multiply: a static overload takes the object"));
    }
  }

  [Test]
  public void Write_KeepsAMemberWhoseStaticOverloadTakesASmartPointer()
  {
    // Arrange (Start() beside static Start(smart pointer): SWIG tells a smart pointer from the
    // object)
    var progress = new ClassModel("Demo_Progress", "Demo_Progress.h", ["vtkObjectBase"],
                                  TransientTraits, [], [
                                    Method("Start", Builtin("int")),
                                    Method("Start", Builtin("int"),
                                           new ParameterModel(
                                             "theProgress",
                                             Ref(Const(SmartPointer("Demo_Progress"))), null)) with
                                    {
                                      IsStatic = true
                                    }
                                  ]);

    // Act
    var module = Write(Package(classes: [progress]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  int Start();"));
      Assert.That(module.Interface,
                  Does.Contain(
                    "  static int Start(const vtkSmartPointer<Demo_Progress>& theProgress);"));
    }
  }

  [Test]
  public void Write_KeepsAMemberWhoseStaticOverloadIsLeftOut()
  {
    // Arrange (the static overload doesn't link, so nothing shadows the instance member)
    var matrix = ValueClass("Demo_Mat",
                            Method("Multiply", Builtin("void"),
                                   new ParameterModel("theOther", Ref(Const(Class("Demo_Mat"))),
                                                      null)), Method(
                                "Multiply", Class("Demo_Mat"),
                                new ParameterModel("theA", Ref(Const(Class("Demo_Mat"))), null),
                                new ParameterModel("theB", Ref(Const(Class("Demo_Mat"))), null))
                              with
                              {
                                IsStatic = true, IsCallable = false
                              });

    // Act
    var module = Write(Package(classes: [matrix]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("  void Multiply(const Demo_Mat& theOther);"));
      Assert.That(module.Skipped, Has.None.Contains("a static overload takes the object"));
    }
  }

  [Test]
  public void Write_CopiesConstReturnsOfAnEmptyValueClass()
  {
    // Arrange (no constructors or members: C# gets it only from a static member's const& return, as
    // a copy)
    var order = ValueClass("Demo_Order");

    // Act
    var module = Write(Package(classes: [order]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("%netvtk_valueclass(Demo_Order)"),
                  "the copy typemaps");
      Assert.That(module.Interface, Does.Contain("class Demo_Order {\npublic:\n};"));
      Assert.That(module.Skipped, Is.Empty);
    }
  }

  [Test]
  public void Write_LogsANamespaceWhoseCSharpNameAClassHas()
  {
    // Arrange (namespace Demo_Tools's static class would be Demo_Tools, the name of another
    // package's class)
    _registry.AddClass(new KnownClass("Demo_Tools", "Other", WrapKind.ValueClass));
    var package = new PackageModel("Demo", ["Demo.h"], [], [], [], [
      new FunctionModel("Demo_Tools", "Scale", Builtin("double"), [], IsDeprecated: false)
    ]);

    // Act
    var module = Write(package);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Skipped, Is.EqualTo(new[]
      {
        "namespace Demo_Tools: class Demo_Tools (Other) has its C# name"
      }));
      Assert.That(module.Namespaces, Is.Empty);
      Assert.That(module.Interface, Does.Not.Contain("NetVtk_Demo_Tools"));
    }
  }

  [Test]
  public void Write_CreatesNoObjectItsProxyCouldntDelete()
  {
    // Arrange (a protected destructor: the proxy couldn't release what a constructor made)
    var scope = new ClassModel("Demo_Scope", "Demo_Scope.h", [],
                               ValueTraits with { HasPublicDestructor = false },
                               [new ConstructorModel([], IsDeprecated: false)],
                               [Method("Depth", Builtin("int")) with { IsConst = true }]);

    // Act
    var module = Write(Package(classes: [scope]));

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("%nodefaultdtor Demo_Scope;"));
      Assert.That(module.Interface,
                  Does.Contain("class Demo_Scope {\npublic:\n  int Depth() const;\n};"));
      Assert.That(module.Skipped, Is.EqualTo(new[]
      {
        "Demo_Scope::Demo_Scope: the destructor isn't public or doesn't link, so C# couldn't release the object"
      }));
    }
  }

  [Test]
  public void Write_IncludesThePreludesFirst()
  {
    // Arrange (Demo's headers need Demo_Macro.h first, and CommonDataModel's its module header: a
    // module using CommonDataModel needs both)
    var move = Method("Move", Builtin("void"),
                      new ParameterModel("theTarget", Ref(Const(Class("vtkVector3d"))), null));
    var config = new PackageConfig { Prelude = ["Demo_Macro.h"] };
    Dictionary<string, IReadOnlyList<string>> preludes = new()
      { ["CommonDataModel"] = ["vtkCommonDataModelModule.h"] };

    // Act
    var header = Write(Package(classes: [ValueClass("Demo_Mover", move)]), config: config,
                       preludes: preludes)
      .ModuleHeader;

    // Assert
    Assert.That(
      header,
      Does.EndWith(
        "#pragma once\n#include <Demo_Macro.h>\n#include <vtkCommonDataModelModule.h>\n#include <vtkVector3d.h>\n#include <Demo.h>\n"));
  }

  [Test]
  public void Write_KeepsAnArgumentWhoseBaseTheObjectRefersTo()
  {
    // Arrange (Demo_Walker holds a Demo_Graph, and a Demo_SubGraph is one)
    var graph = ValueClass("Demo_Graph");
    var subGraph = new ClassModel("Demo_SubGraph", "Demo_SubGraph.h", ["Demo_Graph"], ValueTraits,
                                  [], []);
    var walker = new ClassModel("Demo_Walker", "Demo_Walker.h", [], ValueTraits, [
      new ConstructorModel(
        [new ParameterModel("theGraph", Ref(Const(Class("Demo_SubGraph"))), null)],
        IsDeprecated: false)
    ], [], Held: [Pointer(Const(Class("Demo_Graph")))]);

    // Act
    var module = Write(Package(classes: [graph, subGraph, walker]));

    // Assert
    Assert.That(module.Interface,
                Does.Contain("  %apply SWIGTYPE & NETVTK_KEEP { const Demo_SubGraph& theGraph };\n"
                             + "  Demo_Walker(const Demo_SubGraph& theGraph);\n  %clear const Demo_SubGraph& theGraph;"));
  }

  [Test]
  public void Write_DeclaresTheVirtualMembersADirectorOverrides()
  {
    // Arrange (Demo_Object, a director, inherits a pure ComputeSelection and a parameterless
    // Clear; its own Compute is protected and pure, its constructor protected)
    var (presentation, selectable, director) = DirectorHierarchy();

    // Act
    var module = Write(Package(classes: [presentation, selectable, director]),
                       directors: ["Demo_Object"]);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("%module(directors=\"1\", dirprot=\"1\") DemoModule"));
      Assert.That(module.Interface, Does.Contain("%netvtk_director(Demo_Object)"));
      Assert.That(module.Interface,
                  Does.Contain("class Demo_Object : public Demo_Selectable {\npublic:\n"
                               + "  virtual bool Accepts(int theMode) const;\n"
                               + "  virtual void ComputeSelection(int theMode) = 0;\n"
                               + "  virtual ~Demo_Object();\n"
                               + "protected:\n"
                               + "  Demo_Object();\n"
                               + "  virtual void Compute(const vtkSmartPointer<Demo_Presentation>& thePrs, int theMode) = 0;\n"
                               + "};"));
      Assert.That(module.Skipped,
                  Has.One.StartsWith(
                    "Demo_Object::Clear: C# can't override it: SWIG runs no code after a callback"));
    }
  }

  [Test]
  public void Write_LeavesTheBasesOfADirectorAsDeclared()
  {
    // Arrange
    var (presentation, selectable, director) = DirectorHierarchy();

    // Act
    var module = Write(Package(classes: [presentation, selectable, director]),
                       directors: ["Demo_Object"]);

    // Assert (pointers to them may be the director's objects: Directors.i gives those back as
    // themselves)
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface,
                  Does.Contain("class Demo_Selectable : public vtkObjectBase {\npublic:\n"
                               + "  void ComputeSelection(int theMode);\n"
                               + "  void Clear();\n"
                               + "};"));
      Assert.That(module.Interface, Does.Contain("%netvtk_directed(Demo_Selectable)"));
      Assert.That(module.Interface, Does.Contain("%netvtk_directed(Demo_Object)"));
      Assert.That(module.Interface, Does.Not.Contain("%netvtk_directed(Demo_Presentation)"));
    }
  }

  [Test]
  public void Write_MarksAConcreteClassDerivingFromADirectorNotAbstract()
  {
    // Arrange (SWIG would count the director's pure Compute, which Demo_Shape overrides where the
    // .i doesn't show it, against Demo_Shape)
    var (presentation, selectable, director) = DirectorHierarchy();
    var shape = new ClassModel("Demo_Shape", "Demo_Shape.h",
                               ["Demo_Object", "Demo_Selectable", "vtkObjectBase"], TransientTraits,
                               [new ConstructorModel([], false)], []);

    // Act
    var module = Write(Package(classes: [presentation, selectable, director, shape]),
                       directors: ["Demo_Object"]);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Contain("%feature(\"notabstract\") Demo_Shape;"));
      Assert.That(module.Interface, Does.Contain("  Demo_Shape();"));
      Assert.That(module.Interface, Does.Not.Contain("%feature(\"notabstract\") Demo_Object;"));
    }
  }

  [Test]
  public void Write_GivesAConcreteDirectorItsOwnConstructors()
  {
    // Arrange (as vtkOutputWindow: New() and a protected constructor, which C# subclasses call; a
    // constructor calling New() would be a second default one)
    var window = new ClassModel("vtkDemoWindow", "vtkDemoWindow.h", ["vtkObjectBase"],
                                TransientTraits, [], [
                                  Method("New", Pointer(Class("vtkDemoWindow"))) with
                                  {
                                    IsStatic = true
                                  },
                                  Method("GetMTime",
                                         new BuiltinType("unsigned long long", false,
                                                         "vtkMTimeType")) with
                                  {
                                    IsVirtual = true
                                  },
                                ],
                                Hidden: [Method("", Builtin("void")) with { IsProtected = true }]);

    // Act
    var module = Write(Package(classes: [window]), directors: ["vtkDemoWindow"]);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(module.Interface, Does.Not.Contain("%vtk_new(vtkDemoWindow)"));
      Assert.That(module.Interface, Does.Contain("protected:\n  vtkDemoWindow();"));
      Assert.That(module.Interface, Does.Contain("  virtual vtkMTimeType GetMTime();"),
                  "as VTK declares it: an override of unsigned long long doesn't compile on Linux");
    }
  }

  [Test]
  public void Write_RejectsADirectorWhosePureVirtualMemberCSharpCantOverride()
  {
    // Arrange (a stream parameter doesn't reach a C# override)
    var director = new ClassModel("Demo_Writer", "Demo_Writer.h", ["vtkObjectBase"],
                                  TransientTraits with { IsAbstract = true },
                                  [new ConstructorModel([], false)], [
                                    Method("Write", Builtin("void"),
                                           new ParameterModel("theStream", OStream, null)) with
                                    {
                                      IsVirtual = true, IsPure = true
                                    }
                                  ]);

    // Act
    Action write = () => Write(Package(classes: [director]), directors: ["Demo_Writer"]);

    // Assert
    Assert.That(
      write,
      Throws.InvalidOperationException.With.Message.Contains("Demo_Writer can't be a director"));
  }

  // Demo_Object (a director) : Demo_Selectable : vtkObjectBase, and the Demo_Presentation its
  // Compute takes
  private static (ClassModel Presentation, ClassModel Selectable, ClassModel Director)
    DirectorHierarchy()
  {
    var abstractTransient = TransientTraits with { IsAbstract = true };
    var mode = new ParameterModel("theMode", Builtin("int"), null);
    var presentation = new ClassModel("Demo_Presentation", "Demo_Presentation.h", ["vtkObjectBase"],
                                      TransientTraits, [], []);
    var selectable = new ClassModel("Demo_Selectable", "Demo_Selectable.h", ["vtkObjectBase"],
                                    abstractTransient, [], [
                                      Method("ComputeSelection", Builtin("void"), mode) with
                                      {
                                        IsVirtual = true, IsPure = true
                                      },
                                      Method("Clear", Builtin("void")) with { IsVirtual = true },
                                    ]);
    var director = new ClassModel("Demo_Object", "Demo_Object.h",
                                  ["Demo_Selectable", "vtkObjectBase"], abstractTransient, [], [
                                    Method("Accepts", Builtin("bool"), mode) with
                                    {
                                      IsVirtual = true, IsConst = true
                                    },
                                  ], Hidden:
                                  [
                                    Method("", Builtin("void")) with { IsProtected = true },
                                    Method("Compute", Builtin("void"),
                                           new ParameterModel(
                                             "thePrs",
                                             Ref(Const(SmartPointer("Demo_Presentation"))), null),
                                           mode) with
                                    {
                                      IsVirtual = true, IsPure = true, IsProtected = true
                                    },
                                  ]);
    return (presentation, selectable, director);
  }

  // the package's classes registered as Generate registers them
  private GeneratedModule Write(PackageModel package,
                                Dictionary<string, IReadOnlyList<string>>? imports = null,
                                Dictionary<string, IReadOnlyCollection<string>>? bases = null,
                                PackageConfig? config = null,
                                Dictionary<string, IReadOnlyList<string>>? preludes = null,
                                IReadOnlyCollection<string>? directors = null)
  {
    foreach (var c in package.Classes)
    {
      _registry.AddClass(new KnownClass(c.Name, package.Name, ClassRules.KindOf(c), c.Header,
                                        ClassRules.IsMoveOnly(c)));
    }

    var writer = new InterfaceWriter(_registry, new SignatureMapper(_registry), "9.7.1",
                                     preludeOf: p => preludes?.GetValueOrDefault(p) ?? [],
                                     classOf: name =>
                                       package.Classes.FirstOrDefault(c => c.Name == name),
                                     directorClasses: directors);
    return writer.Write(package, config ?? new PackageConfig(),
                        p => imports?.GetValueOrDefault(p) ?? [],
                        basesOf: bases is null ? null : p => bases.GetValueOrDefault(p) ?? []);
  }

  private static PackageModel Package(List<EnumModel>? enums = null,
                                      List<ClassModel>? classes = null) =>
    new("Demo", ["Demo.h"], enums ?? [], classes ?? [], [], []);

  private static ClassModel ValueClass(string name, params MethodModel[] methods) =>
    new(name, $"{name}.h", [], ValueTraits, [], methods);

  private static MethodModel Method(string name, CppType returns,
                                    params ParameterModel[] parameters) =>
    new(name, returns, parameters, IsStatic: false, IsConst: false, IsVirtual: false,
        IsDeprecated: false);
}
