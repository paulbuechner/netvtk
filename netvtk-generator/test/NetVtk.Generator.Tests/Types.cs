// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

//
using NetVtk.Generator.Mapping;
using NetVtk.Generator.Model;

namespace NetVtk.Generator.Tests;

/// <summary>Model types and a registry for tests, spelled like the C++ they stand for.</summary>
internal static class Types
{
  /// <summary>A copyable class, which C# gets as a value class (vtkVariant).</summary>
  public static readonly ClassTraits ValueTraits =
    new(IsVtkObject: false, IsAbstract: false, HasPublicDestructor: true, IsCopyable: true);

  /// <summary>A VTK object, which counts its references (vtkTransform).</summary>
  public static readonly ClassTraits VtkObjectTraits =
    new(IsVtkObject: true, IsAbstract: false, HasPublicDestructor: true, IsCopyable: false);

  public static NamedType Class(string name) => new(name, NamedKind.Class, []);

  public static NamedType Enum(string name) => new(name, NamedKind.Enum, []);

  public static NamedType SmartPointer(string name) =>
    new("vtkSmartPointer", NamedKind.Class, [Class(name)]);

  public static NamedType Template(string name, string argument) => Template(name, Class(argument));

  public static NamedType Template(string name, params CppType[] arguments) =>
    new(name, NamedKind.Class, arguments);

  /// <summary>
  /// <c>ostream&amp;</c> as libclang spells it: <c>std::basic_ostream&lt;char,
  /// std::char_traits&lt;char&gt;&gt;&amp;</c>.
  /// </summary>
  public static ReferenceType OStream => Ref(Template("std::basic_ostream", Builtin("char"),
                                                      Template("std::char_traits",
                                                               Builtin("char"))));

  public static BuiltinType Builtin(string name) => new(name);

  public static CppType Const(CppType type) => type switch
  {
    BuiltinType b => b with { Const = true },
    NamedType n => n with { Const = true },
    PointerType p => p with { Const = true },
    _ => type,
  };

  public static ReferenceType Ref(CppType type) => new(type);

  public static PointerType Pointer(CppType type) => new(type);

  /// <summary>
  /// vtkVector3d (value type), vtkVariant (value class), vtkTransform (a VTK object), a plain class
  /// and an enum, each in a module of its own but for CommonCore's root and enum.
  /// </summary>
  public static TypeRegistry Registry()
  {
    var registry = new TypeRegistry();
    registry.AddClass(new KnownClass("vtkObjectBase", "CommonCore", WrapKind.VtkObject));
    registry.AddClass(new KnownClass("vtkVector3d", "CommonDataModel", WrapKind.ValueType));
    registry.AddClass(new KnownClass("vtkVariant", "CommonCore", WrapKind.ValueClass));
    registry.AddClass(new KnownClass("vtkTransform", "CommonTransforms", WrapKind.VtkObject));
    registry.AddClass(new KnownClass("vtkTimerLogScope", "CommonSystem", WrapKind.Plain));
    registry.AddEnum("vtkEventDataDevice", "CommonCore");
    return registry;
  }
}
