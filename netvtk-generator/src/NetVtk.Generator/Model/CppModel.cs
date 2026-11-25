// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace NetVtk.Generator.Model;

/// <summary>
/// One VTK module as the parser saw it: public declarations from the module's own headers only.
/// Plain records, so mapping and emitting never touch libclang.
/// </summary>
/// <param name="Headers">The headers that compile, in include order.</param>
/// <param name="ExcludedHeaders">
/// Package headers left out because they don't compile on their own, with the first error.
/// </param>
/// <param name="ExcludedTypes">
/// Types left out, with the reason: a nested or namespace type whose flat name is a file-scope name
/// too, a class template instance that doesn't instantiate, that C++ can't name outside its class,
/// or whose arguments differ per platform.
/// </param>
internal sealed record PackageModel(string Name, IReadOnlyList<string> Headers,
                                    IReadOnlyList<EnumModel> Enums,
                                    IReadOnlyList<ClassModel> Classes,
                                    IReadOnlyList<TypedefModel> Typedefs,
                                    IReadOnlyList<FunctionModel> NamespaceFunctions,
                                    IReadOnlyList<string>? ExcludedHeaders = null,
                                    IReadOnlyList<string>? ExcludedTypes = null);

/// <param name="Name">
/// The name in the generated code: flat for a nested or namespace enum (see
/// <see cref="ClassModel"/>).
/// </param>
/// <param name="QualifiedName">
/// C++'s name of a nested or namespace enum (<c>vtkCommand::EventIds</c>), otherwise null.
/// </param>
/// <param name="Underlying">
/// The underlying type when it isn't int (<c>enum class Domain : uint8_t</c>: unsigned char),
/// otherwise null.
/// </param>
/// <param name="IsScoped">
/// An <c>enum class</c>: its constants are unique only in it, so the .i declares it so too.
/// </param>
internal sealed record EnumModel(string Name, string Header, IReadOnlyList<EnumConstant> Constants,
                                 string? QualifiedName = null, string? Underlying = null,
                                 bool IsScoped = false);

internal sealed record EnumConstant(string Name, long Value);

internal sealed record TypedefModel(string Name, string Header, CppType Target);

/// <summary>A class or struct definition, with its public API.</summary>
/// <param name="Name">
/// The name in the generated code. A nested or namespace class has a flat one, its scopes joined by
/// '_' (<c>vtkTextRenderer::Metrics</c> is <c>vtkTextRenderer_Metrics</c>), which the package's
/// nested header declares as an alias.
/// </param>
/// <param name="Ancestors">
/// Public base classes, nearest first (depth-first along the first base): the generator derives
/// from the nearest wrapped one.
/// </param>
/// <param name="Layout">
/// Size and alignment as libclang computes them, for the layout guards of value types.
/// </param>
/// <param name="Fields">
/// The data members, public or not, in declaration order: a value type's C# struct mirrors them.
/// </param>
/// <param name="Operators">
/// Public overloaded operators (<c>operator+</c>, ...): C# operators of value types.
/// </param>
/// <param name="Hidden">
/// Non-public methods, and constructors under an empty name (the class's may change: an alias names
/// an instance). They're never wrapped, but take part in C++ overload resolution, which comes
/// before the access check.
/// </param>
/// <param name="IsDeprecated">
/// VTK marks the class deprecated (<c>VTK_DEPRECATED_IN_9_7_0</c>): its proxy is
/// <c>[Obsolete]</c>, like deprecated members.
/// </param>
/// <param name="QualifiedName">
/// C++'s name of a nested or namespace class, otherwise null (an alias-named template instance
/// too).
/// </param>
/// <param name="IsPlainData">
/// Public data only, every field a number, an enum, a value type or plain data itself
/// (<c>vtkPriorityQueue::Item</c>: a priority and an id): a C# struct, like the configured value
/// types.
/// </param>
/// <param name="Instance">
/// For a class template instance no alias names (<see cref="NamedType.InstanceName"/>): C++'s
/// spelling of it (<c>vtkQuaternion&lt;double&gt;</c>) and the headers its alias needs (the
/// template's, its arguments'). Its first user in package order owns it; otherwise null.
/// </param>
/// <param name="Held">
/// What the object refers to without owning it: the types of the pointer and reference fields of
/// the class and of its bases, whatever their access (template instances too; not the standard
/// library's containers, whose pointers are their storage). An argument of such a class must
/// outlive the object.
/// </param>
/// <param name="IsStruct">
/// Declared with <c>struct</c>: VTK's reference documents it as a struct.
/// </param>
/// <param name="Template">
/// For a class template instance, or a class in one, the template by its C++ name without arguments
/// (<c>vtkSOADataArrayTemplate</c>): what VTK's reference documents. Otherwise null.
/// </param>
internal sealed record ClassModel(string Name, string Header, IReadOnlyList<string> Ancestors,
                                  ClassTraits Traits, IReadOnlyList<ConstructorModel> Constructors,
                                  IReadOnlyList<MethodModel> Methods, ClassLayout? Layout = null,
                                  IReadOnlyList<FieldModel>? Fields = null,
                                  IReadOnlyList<MethodModel>? Operators = null,
                                  IReadOnlyList<MethodModel>? Hidden = null,
                                  bool IsDeprecated = false, string? QualifiedName = null,
                                  bool IsPlainData = false, ClassInstance? Instance = null,
                                  IReadOnlyList<CppType>? Held = null, bool IsStruct = false,
                                  string? Template = null);

/// <param name="Spelling">C++'s spelling of the instance.</param>
/// <param name="Includes">The headers that declare the template and its arguments.</param>
/// <param name="Type">
/// The instance as signatures use it: the registry resolves it to the class.
/// </param>
/// <param name="Alias">
/// What the owner's nested header aliases when it isn't <paramref name="Spelling"/>: a public
/// typedef in a class that names an instance of a protected template (<c>Outer::Curve</c>).
/// </param>
internal sealed record ClassInstance(string Spelling, IReadOnlyList<string> Includes,
                                     NamedType Type, string? Alias = null);

internal sealed record ClassLayout(long Size, long Align);

/// <param name="Offset">Byte offset in the object, as libclang lays it out.</param>
/// <param name="Size">The field's size in bytes.</param>
/// <param name="Members">
/// For a field of class type, that class's fields, offsets relative to this field (recorded for
/// value types only): a value type's C# struct flattens fields whose class isn't a value type
/// itself (<c>vtkVector3d</c>).
/// </param>
/// <param name="IsPublic">Public in C++: a plain-data struct's fields are public in C# too.</param>
internal sealed record FieldModel(string Name, CppType Type, long Offset, long Size = 0,
                                  IReadOnlyList<FieldModel>? Members = null, bool IsPublic = false);

/// <summary>Facts about a class that decide how it is wrapped.</summary>
/// <param name="IsVtkObject">Derives from vtkObjectBase: a VTK object, reference-counted.</param>
/// <param name="IsAbstract">Has pure virtual methods: no constructors.</param>
/// <param name="HasPublicDestructor">
/// Public destructor and a usable <c>operator delete</c>; otherwise SWIG needs %nodefaultdtor.
/// </param>
/// <param name="IsCopyable">
/// A copy compiles (public copy constructor, or an implicit one whose members copy): a value class.
/// </param>
/// <param name="IsCreatable">
/// <c>new T(...)</c> compiles: false when the class declares only placement forms of <c>operator
/// new</c> (nodes an allocator makes). Such a class gets no constructors and is never copied into
/// a proxy.
/// </param>
/// <param name="HasDefaultConstructor">
/// <c>T()</c> compiles. Without, SWIG must know (%nodefaultctor): it default-constructs by-value
/// results otherwise.
/// </param>
/// <param name="IsMovable">
/// A public move constructor: a move-only class (a scoped guard) returned by value moves into the
/// proxy.
/// </param>
/// <param name="DeclaresDestructor">
/// The class declares its destructor, which may use what the object refers to (a scope's closes
/// it); an implicit one doesn't.
/// </param>
internal sealed record ClassTraits(bool IsVtkObject, bool IsAbstract, bool HasPublicDestructor,
                                   bool IsCopyable, bool IsCreatable = true,
                                   bool HasDefaultConstructor = true, bool IsMovable = false,
                                   bool DeclaresDestructor = false);

/// <param name="IsCallable">
/// Links from the shim: exported by the VTK libraries, or defined in the headers and calling only
/// what links; otherwise <paramref name="Unlinked"/> names what doesn't (the function itself, or a
/// function its inline definition calls).
/// </param>
/// <param name="ThroughVtable">
/// <paramref name="Unlinked"/> is a virtual function: the inline definition sets the vtable of a
/// class the libraries don't export, which the shim then emits (MSVC), naming every virtual
/// function.
/// </param>
/// <param name="Broken">
/// A member of a class template instance whose body doesn't compile for it (C++'s error): templates
/// instantiate a member only when a call needs it, and the wrapper's would.
/// </param>
internal sealed record ConstructorModel(IReadOnlyList<ParameterModel> Parameters, bool IsDeprecated,
                                        bool IsCallable = true, string? Unlinked = null,
                                        bool ThroughVtable = false, string? Broken = null);

/// <param name="IsCallable">See <see cref="ConstructorModel"/>.</param>
/// <param name="Broken">See <see cref="ConstructorModel"/>.</param>
/// <param name="IsPure">Pure virtual (<c>= 0</c>).</param>
/// <param name="IsProtected">
/// Declared protected (<see cref="ClassModel.Hidden"/> holds private members too): a C# subclass
/// of a director class overrides the virtual ones, and calls the constructors.
/// </param>
/// <param name="Hints">
/// VTK's wrapping hints on the method, as libclang reports them (<c>vtk::sizehint(6)</c>,
/// <c>vtk::newinstance</c>, <c>vtk::wrapexclude</c>); null for none.
/// </param>
internal sealed record MethodModel(string Name, CppType Return,
                                   IReadOnlyList<ParameterModel> Parameters, bool IsStatic,
                                   bool IsConst, bool IsVirtual, bool IsDeprecated,
                                   bool IsCallable = true, string? Unlinked = null,
                                   string? Broken = null, bool IsPure = false,
                                   bool IsProtected = false, IReadOnlyList<string>? Hints = null);

/// <summary>A function in a C++ namespace (<c>vtk::to_string()</c> and friends).</summary>
/// <param name="IsCallable">See <see cref="ConstructorModel"/>.</param>
internal sealed record FunctionModel(string Namespace, string Name, CppType Return,
                                     IReadOnlyList<ParameterModel> Parameters, bool IsDeprecated,
                                     bool IsCallable = true, string? Unlinked = null);

/// <param name="Default">
/// The default argument as source text, or null without one. Empty for one the parser can't spell
/// (a macro's that isn't a constant), which declarations drop (<c>MemberRules.Initializers</c>).
/// </param>
/// <param name="Hints">
/// VTK's wrapping hints on the parameter (<c>vtk::zerocopy</c>), or null.
/// </param>
internal sealed record ParameterModel(string Name, CppType Type, string? Default,
                                      IReadOnlyList<string>? Hints = null);
