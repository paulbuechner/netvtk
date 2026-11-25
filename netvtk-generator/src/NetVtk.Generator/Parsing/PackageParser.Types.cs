// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

// ClangSharp
using ClangSharp;
using ClangSharp.Interop;

//
using NetVtk.Generator.Model;

// alias directives
using ClangBuiltinType = ClangSharp.BuiltinType;
using ClangPointerType = ClangSharp.PointerType;
using ClangType = ClangSharp.Type;
using CppArrayType = NetVtk.Generator.Model.ArrayType;
using CppBuiltinType = NetVtk.Generator.Model.BuiltinType;
using CppFunctionType = NetVtk.Generator.Model.FunctionType;
using CppPointerType = NetVtk.Generator.Model.PointerType;
using CppReferenceType = NetVtk.Generator.Model.ReferenceType;

namespace NetVtk.Generator.Parsing;

// the part of PackageParser that turns ClangSharp types into model types and names
internal sealed partial class PackageParser
{
  // typedefs whose canonical type differs per platform (size_t is unsigned long long on win-x64,
  // unsigned long elsewhere)
  private static readonly Dictionary<string, string> WidthTypedefs = new()
  {
    ["size_t"] = "size_t",
    ["int64_t"] = "long long",
    ["uint64_t"] = "unsigned long long",
    ["int32_t"] = "int",
    ["uint32_t"] = "unsigned int",
    ["int16_t"] = "short",
    ["uint16_t"] = "unsigned short",
    ["uint8_t"] = "unsigned char",
    ["int8_t"] = "signed char",
    // pointer-sized: IntPtr and UIntPtr in C#
    ["intptr_t"] = "intptr_t",
    ["uintptr_t"] = "uintptr_t",
    ["ptrdiff_t"] = "ptrdiff_t",
    // VTK's: 64-bit ids on every architecture (the port sets VTK_USE_64BIT_IDS), long long like
    // vtkTypeInt64 everywhere; vtkMTimeType is unsigned long on 64-bit Linux and macOS
    ["vtkIdType"] = "long long",
    ["vtkMTimeType"] = "unsigned long long",
    ["vtkTypeInt64"] = "long long",
    ["vtkTypeUInt64"] = "unsigned long long",
    ["vtkTypeInt32"] = "int",
    ["vtkTypeUInt32"] = "unsigned int",
    ["vtkTypeInt16"] = "short",
    ["vtkTypeUInt16"] = "unsigned short",
    ["vtkTypeInt8"] = "signed char",
    ["vtkTypeUInt8"] = "unsigned char",
  };

  /// <summary>ClangSharp type to model type: sugar resolved, width typedefs kept.</summary>
  internal static CppType Convert(ClangType type)
  {
    var isConst = type.IsLocalConstQualified;
    switch (type)
    {
      case TypedefType typedef when WidthTypedefs.TryGetValue(typedef.Decl.Name, out var width):
        return new CppBuiltinType(width, isConst,
                                  typedef.Decl.Name == width ? null : typedef.Decl.Name);
      case ElaboratedType elaborated:
        return WithConst(Convert(elaborated.NamedType), isConst);
      // a function pointer by its typedef: its signature and calling convention may differ per
      // platform (vtkThreadFunctionType)
      case TypedefType typedef
        when typedef.CanonicalType is ClangPointerType { PointeeType: FunctionProtoType }
             && typedef.Decl.DeclContext is not ClassTemplateSpecializationDecl
             && Convert(typedef.Decl.UnderlyingType) is CppPointerType function:
        return function with { Const = isConst, Written = typedef.Decl.QualifiedName };
      case TypedefType typedef:
        return WithConst(Convert(typedef.Decl.UnderlyingType), isConst);
      case TemplateSpecializationType { IsTypeAlias: true } alias:
        return WithConst(Convert(alias.AliasedType), isConst);
      case TemplateSpecializationType written when written.IsSugared:
        return WithConst(WrittenWidths(Convert(written.Desugar), written), isConst);
      case ParenType paren:
        return WithConst(Convert(paren.InnerType), isConst);
      case AttributedType attributed:
        return WithConst(Convert(attributed.ModifiedType), isConst);
      case UsingType or SubstTemplateTypeParmType or DecltypeType when type.IsSugared:
        return WithConst(Convert(type.Desugar), isConst);
      case ClangBuiltinType builtin:
        return new CppBuiltinType(builtin.UnqualifiedType.AsString, isConst);
      case ClangPointerType pointer:
        return new CppPointerType(Convert(pointer.PointeeType), isConst);
      case ConstantArrayType array:
        return new CppArrayType(Convert(array.ElementType), array.Size);
      // a parameter declared as an array (double x[], double bounds[6]) is a pointer, which the
      // declaration tells
      case DecayedType decayed:
        return Convert(decayed.OriginalType);
      case IncompleteArrayType array:
        return new CppArrayType(Convert(array.ElementType), 0);
      case FunctionProtoType function:
        return new CppFunctionType(Convert(function.ReturnType),
                                   [.. function.ParamTypes.Select(Convert)], function.IsVariadic);
      case LValueReferenceType lvalue:
        return new CppReferenceType(Convert(lvalue.PointeeType));
      case RValueReferenceType rvalue:
        return new CppReferenceType(Convert(rvalue.PointeeType), IsRValue: true);
      // an enum in a class template instance (vtkSOADataArrayTemplate<float>::StorageTypeEnum):
      // the instance's flat name and its own
      case EnumType e when NestedInstanceName(e.Decl) is { } nestedEnum:
        return new NamedType(nestedEnum, NamedKind.Enum, [], isConst);
      case EnumType e when FlatName(e.Decl.QualifiedName) is var flatEnum
                           && flatEnum != e.Decl.QualifiedName
                           && IsFileScopeName(e.Decl, flatEnum):
        return new UnsupportedType($"{e.Decl.QualifiedName}, whose flat name {flatEnum} is taken");
      case EnumType e:
        return new NamedType(FlatName(e.Decl.QualifiedName), NamedKind.Enum, [], isConst);
      case RecordType { Decl: ClassTemplateSpecializationDecl specialization }
        when InstanceAlias(specialization) is { } alias:
        return new NamedType(alias, NamedKind.Class, [], isConst);
      // a template in a class template instance (Outer<T>::Inner<true>) has no name of its own
      case RecordType { Decl: ClassTemplateSpecializationDecl specialization }
        when TemplateName(specialization.QualifiedName).Contains('<'):
        return new UnsupportedType(
          $"{specialization.QualifiedName}, a template in a class template instance");
      case RecordType { Decl: ClassTemplateSpecializationDecl specialization }:
        // not SpecializedTemplate: ClangSharp's factory casts partial specializations (std
        // internals) to ClassTemplateDecl and throws
        return new NamedType(FlatName(TemplateName(specialization.QualifiedName)), NamedKind.Class,
                             [.. specialization.TemplateArgs.Select(TemplateArgument)], isConst);
      // only declared here (class vtkRenderer;): another package may define it, under the flat
      // name
      case RecordType { Decl: { Definition: null } declared }:
        return new NamedType(FlatName(declared.QualifiedName), NamedKind.Class, [], isConst,
                             declared.QualifiedName);
      // a class in a class template instance (Outer<T>::Iterator): the instance's flat name and
      // its own
      case RecordType { Decl: { DeclContext: ClassTemplateSpecializationDecl } nested }
        when NestedInstanceName(nested) is { } nestedName:
        return new NamedType(nestedName, NamedKind.Class, [], isConst);
      case RecordType record when FlatName(record.Decl.QualifiedName) is var flatRecord
                                  && flatRecord != record.Decl.QualifiedName
                                  && IsFileScopeName(record.Decl, flatRecord):
        return new UnsupportedType(
          $"{record.Decl.QualifiedName}, whose flat name {flatRecord} is taken");
      case RecordType record:
        return new NamedType(FlatName(record.Decl.QualifiedName), NamedKind.Class, [], isConst);
      default:
        return new UnsupportedType(type.AsString);
    }
  }

  /// <summary>
  /// A standard library instance with the arguments its template's written ones resolve to where
  /// they are numbers or standard library instances themselves (<c>std::vector&lt;vtkIdType&gt;</c>
  /// keeps <c>vtkIdType</c>): an instance's own arguments are canonical, the generating platform's
  /// (<c>size_t</c> is <c>unsigned long long</c> on win-x64), which the wrappers can't spell. Other
  /// instances keep theirs: the classes they are go by them.
  /// </summary>
  private static CppType WrittenWidths(CppType canonical, TemplateSpecializationType written)
  {
    if (canonical is not NamedType { TemplateArguments.Count: > 0 } instance
        || !instance.Name.StartsWith("std::", StringComparison.Ordinal))
    {
      return canonical;
    }

    List<CppType> arguments = [.. instance.TemplateArguments];
    for (var i = 0; i < Math.Min(arguments.Count, written.Args.Count); i++)
    {
      if (written.Args[i].Kind != CXTemplateArgumentKind.CXTemplateArgumentKind_Type)
      {
        continue;
      }

      var argument = Convert(written.Args[i].AsType);
      if ((argument is CppBuiltinType && arguments[i] is CppBuiltinType)
          || (argument is NamedType w && arguments[i] is NamedType c && w.Name == c.Name))
      {
        arguments[i] = argument;
      }
    }

    return instance with { TemplateArguments = arguments };
  }

  /// <summary>
  /// The flat name of a class or enum in a class template instance: the instance's
  /// (<see cref="NamedType.InstanceName"/>) and its own
  /// (<c>vtkSOADataArrayTemplate&lt;float&gt;::StorageTypeEnum</c> is
  /// <c>vtkSOADataArrayTemplate_float_StorageTypeEnum</c>); null for a nested template.
  /// </summary>
  internal static string? NestedInstanceName(TagDecl nested) =>
    nested is not ClassTemplateSpecializationDecl
    && nested.DeclContext is ClassTemplateSpecializationDecl outer
    && Convert(outer.TypeForDecl) is NamedType { TemplateArguments.Count: > 0 } instance
      ? $"{instance.InstanceName}_{nested.Name}"
      : null;

  /// <summary>
  /// The name of a type in the generated code: C++'s qualified name, the scopes of a nested or
  /// namespace type joined by '_' (<c>vtkTextRenderer::Metrics</c> is
  /// <c>vtkTextRenderer_Metrics</c>). The standard library's stay.
  /// </summary>
  internal static string FlatName(string qualifiedName) =>
    qualifiedName.StartsWith("std::", StringComparison.Ordinal)
      ? qualifiedName
      : qualifiedName.Replace("::", "_", StringComparison.Ordinal);

  // a template argument: a type, or a constant as C++ spells it (an enumerator by its flat name)
  private static CppType TemplateArgument(TemplateArgument argument)
  {
    if (argument.Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Type)
    {
      return Convert(argument.AsType);
    }

    if (argument.Kind != CXTemplateArgumentKind.CXTemplateArgumentKind_Integral)
    {
      return new UnsupportedType("non-type template argument");
    }

    var value = argument.AsIntegral;
    return new ConstantArgument(argument.IntegralType.CanonicalType switch
    {
      EnumType { Decl: var e } => e.Enumerators.FirstOrDefault(c => c.InitVal == value) is
        { } constant
        ? $"{FlatName(e.QualifiedName)}::{constant.Name}"
        : $"static_cast<{FlatName(e.QualifiedName)}>({value})",
      ClangBuiltinType { Kind: CXTypeKind.CXType_Bool } => value != 0 ? "true" : "false",
      _ => value.ToString(CultureInfo.InvariantCulture),
    });
  }

  // the names declared at file scope in a translation unit (in extern "C" blocks too), once per
  // unit
  private static readonly ConditionalWeakTable<TranslationUnit, HashSet<string>> FileScopeNames =
    new();

  private static bool IsFileScopeName(Decl decl, string name) =>
    FileScopeNames.GetValue(decl.TranslationUnit, static unit =>
      [
        .. FileScope(unit.TranslationUnitDecl.Decls).OfType<NamedDecl>().Select(d => d.Name)
      ])
      .Contains(name);

  private static IEnumerable<Decl> FileScope(IEnumerable<Decl> decls) =>
    decls.SelectMany(d => d is LinkageSpecDecl linkage ? FileScope(linkage.Decls) : [d]);

  // class template instances named by an alias at file scope in the template's own header (using
  // vtkFooD = vtkFoo<double>, next to vtkFoo), the first one each: the instance is a class of that
  // name. Every unit that sees the template sees the alias, so they all agree. Once per unit.
  private static readonly ConditionalWeakTable<TranslationUnit, Dictionary<string, string>>
    InstanceAliases = new();

  private static string? InstanceAlias(ClassTemplateSpecializationDecl instance) =>
    InstanceAliases.GetValue(instance.TranslationUnit, static unit => AliasesIn(unit))
      .GetValueOrDefault(instance.TypeForDecl.CanonicalType.AsString);

  private static Dictionary<string, string> AliasesIn(TranslationUnit unit)
  {
    Dictionary<string, string> aliases = [];
    foreach (var alias in FileScope(unit.TranslationUnitDecl.Decls).OfType<TypedefNameDecl>())
    {
      if (alias.UnderlyingType.CanonicalType is RecordType
          {
            Decl: ClassTemplateSpecializationDecl instance
          }
          && !instance.QualifiedName.StartsWith("std::", StringComparison.Ordinal)
          && FileOf(alias) == FileOf(instance))
      {
        aliases.TryAdd(instance.TypeForDecl.CanonicalType.AsString, alias.Name);
      }
    }

    return aliases;
  }

  private static string FileOf(Decl decl)
  {
    decl.Location.GetFileLocation(out var file, out _, out _, out _);
    return Consume(file.Name);
  }

  // the same size for every target: no size_t or its kin (ptrdiff_t), no long (4 bytes on
  // Windows, 8 elsewhere), wchar_t or long double. Only the typedefs tell; the canonical type is
  // the generating platform's.
  private static bool IsFixedSize(ClangType type)
  {
    var t = type;
    for (var depth = 0; depth < MaxSugar && t.IsSugared; depth++, t = t.Desugar)
    {
      if (t is TypedefType
          {
            Decl.Name: "size_t" or "ssize_t" or "ptrdiff_t" or "intptr_t" or "uintptr_t"
          })
      {
        return false;
      }
    }

    return t.CanonicalType is not ClangBuiltinType
    {
      Kind: CXTypeKind.CXType_Long or CXTypeKind.CXType_ULong or CXTypeKind.CXType_WChar
      or CXTypeKind.CXType_LongDouble,
    };
  }

  // without typedefs, elaborations and the like, but with its parts as written (an array's element
  // type)
  private static ClangType Desugared(ClangType type)
  {
    var t = type;
    for (var depth = 0; depth < MaxSugar && t.IsSugared; depth++)
    {
      t = t.Desugar;
    }

    return t;
  }

  // std::array<T, N>'s one member, the T[N]
  private static ClangType? StdArrayElements(ClangType type) =>
    type.CanonicalType is RecordType
    {
      Decl: ClassTemplateSpecializationDecl { Fields: [{ Type: var elements }] } array
    }
    && TemplateName(array.QualifiedName) == "std::array"
    && elements.CanonicalType is ConstantArrayType
      ? elements
      : null;

  // an instance's template, its own argument list off (the one at the end): Outer<T>::Inner<true>
  // is an instance of Outer<T>::Inner
  private static string TemplateName(string qualifiedName)
  {
    if (!qualifiedName.EndsWith('>'))
    {
      return qualifiedName;
    }

    for (int i = qualifiedName.Length - 1, depth = 0; i >= 0; i--)
    {
      depth += qualifiedName[i] switch { '>' => 1, '<' => -1, _ => 0 };
      if (depth == 0)
      {
        return qualifiedName[..i];
      }
    }

    return qualifiedName;
  }

  private static CppType WithConst(CppType type, bool isConst) => !isConst || type.IsConst
    ? type
    : type switch
    {
      CppBuiltinType b => b with { Const = true },
      NamedType n => n with { Const = true },
      CppPointerType p => p with { Const = true },
      _ => type,
    };
}
