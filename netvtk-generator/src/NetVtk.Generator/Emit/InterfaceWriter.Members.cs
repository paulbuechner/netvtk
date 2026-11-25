// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//
using NetVtk.Generator.Mapping;
using NetVtk.Generator.Model;

// static usings
using static NetVtk.Generator.Emit.MemberRules;
using static NetVtk.Generator.Emit.Overloads;

namespace NetVtk.Generator.Emit;

// the part of InterfaceWriter that declares a class's members and the namespace functions' shims
internal sealed partial class InterfaceWriter
{
  private void WriteClass(StringBuilder body, ClassModel c, ModuleContext module)
  {
    var baseName = c.Name == RootVtkObject
      ? null
      : c.Ancestors.FirstOrDefault(a => registry.Class(a) is { Kind: not WrapKind.ValueType })
        ?? (Kind(c) == WrapKind.VtkObject ? RootVtkObject : null);
    if (baseName is not null && registry.Class(baseName) is { } baseClass)
    {
      module.Uses.Add(baseClass.Use);
      if (baseClass.Package != module.Package)
      {
        module.BasePackages.Add(baseClass.Package);
      }
    }

    body.AppendLine(baseName is null
                      ? $"class {c.Name} {{"
                      : $"class {c.Name} : public {baseName} {{")
      .AppendLine("public:");
    Constructors(body, c, module, c.Constructors, IsDirector(c));
    Methods(body, c, module);
    Fields(body, c, module);
    if (IsDirector(c))
    {
      DirectorMembers(body, c, module);
    }

    body.AppendLine("};").AppendLine();
    foreach (var name in (c.Operators ?? []).Select(o => o.Name)
             .Where(n => !MemberRules.IsCoveredOperator(n, c))
             .Distinct())
    {
      module.Skipped.Add($"{c.Name}::{name}: C# proxies have no operators");
    }
  }

  /// <param name="ctors">
  /// The constructors to declare: the public ones, or a director's protected ones.
  /// </param>
  /// <param name="isDirector">
  /// A director class: SWIG constructs the C++ subclass that calls C# back, so an abstract class
  /// gets its constructors too.
  /// </param>
  private void Constructors(StringBuilder body, ClassModel c, ModuleContext module,
                            IReadOnlyList<ConstructorModel> ctors, bool isDirector = false)
  {
    var label = $"{c.Name}::{c.Name}";
    // an object the shim creates is deleted by its proxy: without a usable destructor, the
    // finalizer would throw
    var deletable = c.Traits.HasPublicDestructor || Kind(c) == WrapKind.VtkObject;
    if (!c.Traits.IsCreatable && ctors.Count > 0)
    {
      module.Skipped.Add($"{label}: the class declares only placement forms of operator new");
    }
    else if (!deletable && !c.Traits.IsAbstract && ctors.Count > 0)
    {
      module.Skipped.Add(
        $"{label}: the destructor isn't public or doesn't link, so C# couldn't release the object");
    }

    if ((c.Traits.IsAbstract && !isDirector) || !c.Traits.IsCreatable || !deletable)
    {
      return;
    }

    var overloads = ctors.Select(o => o.Parameters).ToList();
    var picked = c.Constructors.Select(o => o.Parameters).Concat(HiddenConstructors(c)).ToList();
    var referenced = Referenced(c);
    List<(ConstructorModel Model, MappedMember Member)> constructors = [];
    foreach (var ctor in ctors)
    {
      if (Declarable(module.Config, label, ctor.Parameters, overloads, picked,
                     NotCallable(label, ctor), module.Skipped) is { } declared
          && Map(c.Name, c.Name, declared, module, KeptStream("a constructor")) is { } member)
      {
        constructors.Add((ctor, member));
      }
    }

    foreach (var ((ctor, member), firstDefault) in Claim(constructors, x => x.Member, _ => 0))
    {
      var text = Declare(member, firstDefault, "", module);
      if (ctor.IsDeprecated)
      {
        body.AppendLine($"  %csattributes {text.Signature} {SwigObsolete};");
      }

      // arguments the object keeps a reference to: their proxies go into the new proxy
      // (References.i's NETVTK_KEEP), by type and name, for this declaration only
      var kept = KeptArguments(member, text, referenced);
      if (kept.Count > 0)
      {
        module.Typemaps.Add($"%netvtk_keep_construct({c.Name})");
      }

      foreach (var group in kept.GroupBy(k => k.Pointer))
      {
        body.AppendLine(
          $"  %apply SWIGTYPE {(group.Key ? "*" : "&")} NETVTK_KEEP {{ {string.Join(", ", group.Select(k => k.Declaration))} }};");
      }

      body.AppendLine($"  {text.Declaration};");
      if (kept.Count > 0)
      {
        body.AppendLine($"  %clear {string.Join(", ", kept.Select(k => k.Declaration))};");
      }
    }
  }

  // the classes an object of c refers to by raw pointer or reference, through the fields of the
  // class and its bases: a C# argument of one must outlive the object (an iterator holds what it
  // walks)
  private HashSet<string> Referenced(ClassModel c) =>
    [.. (c.Held ?? []).Select(Target).OfType<string>()];

  // what a pointer or lvalue reference to a proxied class refers to, by its spelling without const.
  // Not a smart pointer (a reference count keeps the object, and a reference to a smart pointer
  // variable is to the call's copy) or a standard library type, which have no proxy to keep
  private string? Target(CppType type) => registry.Resolve(type) switch
  {
    PointerType { Pointee: NamedType { Kind: NamedKind.Class } n } when SignatureMapper.IsProxied(n)
      => (n with { Const = false }).Spelling,
    ReferenceType { IsRValue: false, Referee: NamedType { Kind: NamedKind.Class } n } when
      SignatureMapper.IsProxied(n) => (n with { Const = false }).Spelling,
    _ => null,
  };

  /// <summary>
  /// An argument the object keeps a reference to: a class passed by reference or pointer that the
  /// object refers to, or one of its bases. Proxies only
  /// (<paramref name="mapped"/>): a struct argument is a copy for the call, an address (a class no
  /// header defines) has no proxy to keep.
  /// </summary>
  private bool IsKept(CppType type, MappedType mapped, HashSet<string> referenced)
  {
    if (Target(type) is not { } target
        || mapped.CsType == SignatureMapper.CsAddress
        || registry.Class(target) is { Kind: WrapKind.ValueType })
    {
      return false;
    }

    return referenced.Contains(target)
           || (classOf?.Invoke(target)?.Ancestors ?? []).Any(referenced.Contains);
  }

  // a declaration's kept arguments (IsKept): position, pointer or reference, and the parameter as
  // the declaration writes it
  private List<(int Index, bool Pointer, string Declaration)> KeptArguments(
    MappedMember member, MemberText text, HashSet<string> referenced) =>
  [
    .. member.Parameters.Select((p, i) => (p, i))
      .Where(x => IsKept(x.p.Parameter.Type, x.p.Type, referenced))
      .Select(x => (x.i, registry.Resolve(x.p.Parameter.Type) is PointerType,
                x.p.Type.Declare(text.Arguments[x.i]))),
  ];

  private void Methods(StringBuilder body, ClassModel c, ModuleContext module)
  {
    // a class that holds a stream (a pointer or reference field, its own or a base's) may keep a
    // stream it's given
    var keepsStreams = (c.Held ?? []).Any(t => SignatureMapper.StreamOf(t) is not null
                                               || (t is PointerType { Pointee: var held }
                                                   && SignatureMapper
                                                     .StreamClass(held) is not null));
    List<(MethodModel Model, MappedType Returned, MappedMember Member, Func<string, string>?
      Accessor)> methods = [];
    foreach (var m in c.Methods)
    {
      var label = $"{c.Name}::{m.Name}";
      // the reference count belongs to the proxies (VtkObjects.i): covered, not logged
      if (Kind(c) == WrapKind.VtkObject && RefCountMembers.Contains(m.Name))
      {
        continue;
      }

      // VTK keeps it from every wrapper: not logged, the log holds what C# could reach
      if (m.Hints?.Contains("vtk::wrapexclude") == true)
      {
        continue;
      }

      var named = c.Methods.Where(o => o.Name == m.Name).ToList();
      var twins = named.Where(o => o.IsStatic == m.IsStatic).ToList();
      var clash = m.Name == c.Name || ProxyMembers.Contains(m.Name)
        ? "name clashes with the C# proxy"
        : null;
      if (Declarable(module.Config, label, m.Parameters, twins.Select(o => o.Parameters),
                     named.Select(o => o.Parameters).Concat(Hidden(c, m.Name)),
                     NotCallable(label, m), module.Skipped, clash: clash) is not { } declared)
      {
        continue;
      }

      // a member's references borrow from its object (References.i); a static one has no object to
      // keep alive
      var returned = mapper.Return(m.Return, m.Parameters, m.IsStatic ? null : c.Name, VtkHints(m));
      Func<string, string>? accessor = null;
      if (returned.Type is null && Accessor(c, m) is { } shim)
      {
        (returned, accessor) = shim;
      }

      if (Returned(returned, label, m.Return, m.Parameters,
                   twins.Select(o => (o.Return, o.Parameters)), module.Skipped) is { } returnType
          && Map(c.Name, m.Name, declared, module, keepsStreams ? KeptStream("the class") : null,
                 SizeHints.Of(m, HintsOf(c, m))) is { } member)
      {
        methods.Add((m, returnType, member, accessor));
      }
    }

    var statics = methods.Where(x => x.Model.IsStatic).Select(x => x.Member).ToList();
    foreach (var shadowed in methods
               .Where(x => !x.Model.IsStatic && statics.Any(s => Shadows(s, x.Member, c.Name)))
               .ToList())
    {
      module.Skipped.Add(
        $"{shadowed.Member.Label}: a static overload takes the object and the same arguments, which SWIG's wrappers can't tell apart");
      methods.Remove(shadowed);
    }

    var referenced = Referenced(c);
    foreach (var ((m, returned, member, accessor), firstDefault) in Claim(
               methods, x => x.Member, x => TwinRank(x.Returned)))
    {
      module.Use(returned);
      var text = Declare(member, firstDefault, returned.Spelling + " ", module);
      var (modifier, qualifier) = (m.IsStatic ? "static " : "", m.IsConst ? " const" : "");
      // an accessor is an %extend member SWIG can return, named as the member in C#
      var signature = accessor is null ? text.Signature : $"NetVtk_{text.Signature}";
      // SWIG matches the feature by name, parameters and const, so it marks this overload only
      if (m.IsDeprecated)
      {
        body.AppendLine($"  %csattributes {signature}{qualifier} {SwigObsolete};");
      }

      // arguments a non-const member may keep (a setter): the proxy keeps each until the member's
      // next call replaces it (References.i's %netvtk_keep_argument), for this declaration only
      var kept = m.IsStatic || m.IsConst ? [] : KeptArguments(member, text, referenced);
      foreach (var (index, _, declaration) in kept)
      {
        body.AppendLine(
          $"  %netvtk_keep_argument({m.Name}.{index}, {(declaration.Contains(',') ? $"%arg({declaration})" : declaration)})");
      }

      // a view the member returns, a copy that refers to the member's object: the copy keeps the
      // object (References.i's NETVTK_VIEW), for this declaration only
      var view = m.IsStatic || accessor is not null ? null : ViewPattern(m.Return);
      if (view is not null)
      {
        body.AppendLine($"  %apply {view} {{ {returned.Spelling} {m.Name} }};");
      }

      // a typemap the return applies (a VTK size hint's array), for this declaration only
      if (returned.Apply is { } apply)
      {
        body.AppendLine($"  %apply {apply} {{ {returned.Spelling.Replace("*", " *")}{m.Name} }};");
      }

      // the typemaps of a parameter a VTK size hint names, by its name, for this declaration only
      List<(SizedReference Sized, string Name)> sized =
      [
        .. member.Parameters.Select((p, i) => (p.Type.Sized, Name: text.Arguments[i]))
          .Where(p => p.Sized is not null)
          .Select(p => (p.Sized!, p.Name))
      ];
      foreach (var (size, name) in sized)
      {
        body.AppendLine($"  {size.Macro(name)}");
      }

      if (accessor is null)
      {
        // a director's virtual member: VTK calls a C# override (Directors.i)
        var overridable = IsDirector(c)
                          && m.IsVirtual
                          && !m.IsStatic
                          && Overridable(c, m, member, module);
        body.AppendLine(
          $"  {modifier}{(overridable ? "virtual " : "")}{text.Declaration}{qualifier}{(overridable && m.IsPure ? " = 0" : "")};");
      }
      else
      {
        body.AppendLine($"  %rename({m.Name}) NetVtk_{m.Name};")
          .AppendLine("  %extend {")
          .AppendLine(
            $"    {modifier}{returned.Spelling} {signature}{qualifier} {{ return {accessor(string.Join(", ", text.Arguments))}; }}")
          .AppendLine("  }");
      }

      if (kept.Count > 0)
      {
        body.AppendLine($"  %clear {string.Join(", ", kept.Select(k => k.Declaration))};");
      }

      if (view is not null)
      {
        body.AppendLine($"  %clear {returned.Spelling} {m.Name};");
      }

      if (returned.Apply is not null)
      {
        body.AppendLine($"  %clear {returned.Spelling.Replace("*", " *")}{m.Name};");
      }

      foreach (var (size, name) in sized)
      {
        body.AppendLine($"  %clear {size.Clear(name)};");
      }
    }
  }

  // the member whose size hints naming parameters a member follows: itself, or for an override
  // without any, the nearest one it overrides that has some (VTK hints the base's declaration)
  private MethodModel HintsOf(ClassModel c, MethodModel m)
  {
    if (!m.IsVirtual || SizeHints.Names(m))
    {
      return m;
    }

    List<string> types = [.. m.Parameters.Select(p => p.Type.Spelling)];
    return c.Ancestors.SelectMany(a => classOf?.Invoke(a)?.Methods ?? [])
             .FirstOrDefault(b => b.Name == m.Name
                                  && b.IsVirtual
                                  && SizeHints.Names(b)
                                  && b.Parameters.Select(p => p.Type.Spelling).SequenceEqual(types))
           ?? m;
  }

  /// <summary>
  /// A value class's public data members as C# properties (SWIG's member variables), as
  /// <see cref="SignatureMapper.Field"/> maps them: numbers, enums and strings read and written,
  /// VTK objects read with a reference of their own, vectors of value classes read as arrays of
  /// copies. A value class's proxy owns its copy, which they are the data of; other classes' fields
  /// stay out (a VTK object's state has accessors).
  /// </summary>
  private void Fields(StringBuilder body, ClassModel c, ModuleContext module)
  {
    if (Kind(c) != WrapKind.ValueClass)
    {
      return;
    }

    // a C# member can't have its class's name or another member's
    HashSet<string> taken = [c.Name, .. ProxyMembers, .. c.Methods.Select(m => m.Name)];
    foreach (var field in (c.Fields ?? []).Where(f => f.IsPublic && f.Name.Length > 0))
    {
      var label = $"{c.Name}::{field.Name}";
      var mapped = mapper.Field(field.Type);
      var reason = module.Config.ExcludeMethods.Contains(label) ? "excluded in the config" :
        taken.Contains(field.Name) ? "name clashes with the C# proxy" :
        IsCSharpKeyword(field.Name) ? "its name is a C# keyword" : mapped.Skip;
      if (reason is not null || mapped.Type is not { } type)
      {
        module.Skipped.Add($"{label}: {reason}");
        continue;
      }

      module.Use(type);
      body.AppendLine($"  {type.Declare(field.Name)};");
    }
  }

  // what VTK objects count references with, which the proxies own (VtkObjects.i)
  private static readonly HashSet<string> RefCountMembers =
    ["Register", "UnRegister", "Delete", "FastDelete", "SetReferenceCount"];

  // a member's VTK hints, and two of VTK's conventions: a static New() hands out a reference of its
  // own, as VTK_NEWINSTANCE's members do; a char* a member returns is a string (vtkGetStringMacro),
  // but for the data of a char array (GetPointer, WritePointer)
  private static IReadOnlyList<string>? VtkHints(MethodModel m)
  {
    if (m is { IsStatic: true, Name: "New", Parameters.Count: 0 })
    {
      return [.. m.Hints ?? [], "vtk::newinstance"];
    }

    return m.Return is PointerType { Pointee: BuiltinType { Name: "char" } }
           && !DataPointers.Contains(m.Name)
      ? [.. m.Hints ?? [], "netvtk::string"]
      : m.Hints;
  }

  // members returning a char array's data, which a string would read up to the first NUL
  private static readonly HashSet<string> DataPointers =
    ["GetPointer", "WritePointer", "GetVoidPointer", "GetBuffer", "GetData"];

  // the NETVTK_VIEW pattern of a by-value or const& return of a view, or null. A const& of a
  // move-only class is no copy: its borrowing proxy keeps the object already (References.i)
  private string? ViewPattern(CppType returned) => registry.Resolve(returned) switch
  {
    NamedType n when View(n) is { } view => $"SWIGTYPE {ViewTypemap(view)}",
    ReferenceType { IsRValue: false, Referee: NamedType { IsConst: true } n } when
      registry.Class(n.Name) is { Kind: WrapKind.ValueClass } && View(n) is { } view =>
      $"const SWIGTYPE & {ViewTypemap(view)}",
    _ => null,
  };

  // held until the GC has collected the view (NETVTK_VIEW_HELD) when its destructor may use the
  // object, and when its constructors keep their arguments: %netvtk_keep_construct holds by-value
  // returns, which the member mustn't weaken
  private string ViewTypemap(ClassModel view)
  {
    var referenced = Referenced(view);
    return view.Traits.DeclaresDestructor
           || view.Constructors.Any(c => c.Parameters.Any(p => Target(p.Type) is { } t
                                                               && referenced.Contains(t)))
      ? "NETVTK_VIEW_HELD"
      : "NETVTK_VIEW";
  }

  // a value class whose fields refer to objects C# has proxies of (a view of a container): its
  // copies depend on them, and so do the moved results of a move-only one (a scoped guard's)
  private ClassModel? View(NamedType n) =>
    registry.Class(n.Name) is { Kind: WrapKind.ValueClass } or { IsMoveOnly: true }
    && classOf?.Invoke(n.Name) is { } model
    && Referenced(model).Count > 0
      ? model
      : null;

  /// <summary>
  /// A wrapped static overload that takes the object first and an instance member's arguments after
  /// (<c>vtkPixelExtent::Grow(n)</c> beside static <c>Grow(extent, n)</c>): SWIG's wrappers pass
  /// the object as the first argument, so they can't tell the two apart. As SWIG compares them:
  /// class arguments by their class, without const and references (a smart pointer is another
  /// type), others by their type.
  /// </summary>
  private bool Shadows(MappedMember s, MappedMember m, string owner)
  {
    string Plain(CppType type) =>
      (registry.Resolve(type) is ReferenceType { Referee: var referee }
        ? referee
        : registry.Resolve(type)).WithoutConst()
      .Spelling;

    // VTK passes its objects by pointer: a static taking the object as T* first shadows too
    // (vtkTextActor::SetConstrainedFontSize)
    string First(CppType type) => registry.Resolve(type) is PointerType { Pointee: NamedType n }
      ? n.WithoutConst().Spelling
      : Plain(type);

    if (s.Name != m.Name
        || s.Parameters.Count == 0
        || First(s.Parameters[0].Parameter.Type) != owner)
    {
      return false;
    }

    // SWIG declares an overload per default arity: any two that meet shadow
    List<string> statics = [.. s.Parameters.Skip(1).Select(p => Plain(p.Parameter.Type))];
    List<string> members = [.. m.Parameters.Select(p => Plain(p.Parameter.Type))];
    return Enumerable.Range(m.FirstDefault, m.Parameters.Count - m.FirstDefault + 1)
      .Any(arity => arity + 1 >= s.FirstDefault
                    && arity <= statics.Count
                    && statics.Take(arity).SequenceEqual(members.Take(arity)));
  }

  /// <summary>
  /// A return SWIG can't give as declared, through an accessor that can (an <c>%extend</c> member:
  /// its body's expression of the arguments), or null. A static member's class reference is the
  /// object's address: a proxy that borrows it and keeps no owner, since the object isn't the
  /// caller's (a singleton's instance).
  /// </summary>
  private (TypeMapping Returned, Func<string, string> Body)? Accessor(ClassModel c, MethodModel m)
  {
    string Call(string arguments) => $"{c.Name}::{m.Name}({arguments})";

    return m.Return switch
    {
      ReferenceType
      {
        IsRValue: false, Referee: NamedType { IsConst: false, TemplateArguments.Count: 0 } t
      } when m.IsStatic && registry.Class(t.Name) is { Kind: not WrapKind.ValueType } => (
        mapper.Return(new PointerType(t, Const: true), m.Parameters),
        arguments => $"&{Call(arguments)}"),
      _ => null,
    };
  }

  // namespace functions (vtk::to_string) as static methods of a shim struct, which C# sees as a
  // class of the namespace's name
  private string NamespaceFunctions(PackageModel package, ModuleContext module)
  {
    var (config, skipped) = (module.Config, module.Skipped);
    var text = new StringBuilder();
    foreach (var group in package.NamespaceFunctions.GroupBy(f => f.Namespace))
    {
      var csName = group.Key.Replace("::", "_");
      if (registry.Class(csName) is { } clash)
      {
        skipped.Add($"namespace {group.Key}: class {clash.Name} ({clash.Package}) has its C# name");
        continue;
      }

      List<(FunctionModel Model, MappedType Returned, MappedMember Member)> functions = [];
      foreach (var f in group)
      {
        var label = $"{group.Key}::{f.Name}";
        // an operator at namespace scope (a user-defined literal: vtk::literals::operator""_token)
        // has no C# name
        if (f.Name.StartsWith("operator", StringComparison.Ordinal))
        {
          skipped.Add($"{label}: C# has no namespace operators");
          continue;
        }

        var named = group.Where(o => o.Name == f.Name).ToList();
        if (Declarable(config, label, f.Parameters, named.Select(o => o.Parameters),
                       named.Select(o => o.Parameters), NotCallable(label, f), skipped) is not
            { } declared)
        {
          continue;
        }

        if (Returned(mapper.Return(f.Return, f.Parameters), label, f.Return, f.Parameters,
                     named.Select(o => (o.Return, o.Parameters)), skipped) is { } returnType
            && Map(group.Key, f.Name, declared, module) is { } member)
        {
          functions.Add((f, returnType, member));
        }
      }

      List<string> members = [];
      List<string> deprecated = [];
      foreach (var ((f, returned, member), firstDefault) in Claim(functions, x => x.Member, _ => 0))
      {
        module.Use(returned);
        var memberText = Declare(member, firstDefault, returned.Spelling + " ", module);
        var call = $"{group.Key}::{f.Name}({string.Join(", ", memberText.Arguments)})";
        members.Add(
          $"  static {memberText.Declaration} {{ {(returned.Spelling == "void" ? "" : "return ")}{call}; }}");
        if (f.IsDeprecated)
        {
          deprecated.Add(memberText.Signature);
        }
      }

      if (members.Count == 0)
      {
        continue;
      }

      module.Namespaces.Add(group.Key);
      // the shim sits in the namespace, where the functions' default arguments and unqualified
      // names resolve as declared
      var namespaces = group.Key.Split("::");
      text.AppendLine($"// namespace {group.Key}: static class {csName}")
        .AppendLine($"%rename({csName}) {group.Key}::NetVtk_{csName};");
      // SWIG directives stay out of %inline, whose text the compiler sees too
      foreach (var signature in deprecated)
      {
        text.AppendLine($"%csattributes {group.Key}::NetVtk_{csName}::{signature} {SwigObsolete};");
      }

      text.AppendLine("%inline %{")
        .AppendLine(string.Join(" ", namespaces.Select(n => $"namespace {n} {{")))
        .AppendLine($"struct NetVtk_{csName} {{");
      foreach (var member in members)
      {
        text.AppendLine(member);
      }

      text.AppendLine("};")
        .AppendLine(string.Join(" ", namespaces.Select(_ => "}")))
        .AppendLine("%}")
        .AppendLine();
    }

    return text.ToString();
  }

  // Streams.i lends a C# stream for the call only
  private static string KeptStream(string keeper) =>
    $"{keeper} may keep the stream, which C# lends for the call only";

  // a member's mapped parameters, or null (skipped, reason logged). keptStream: why a stream
  // parameter can't be lent here; sized: the parameters size hints name (MapParameters).
  private MappedMember? Map(string owner, string name, IReadOnlyList<ParameterModel> parameters,
                            ModuleContext module, string? keptStream = null,
                            IReadOnlyDictionary<int, string?>? sized = null)
  {
    var label = $"{owner}::{name}";
    return MapParameters(mapper, label, parameters, MayKeep(owner, name), module.Skipped,
                         keptStream, sized) is { } mapped
      ? new MappedMember(label, name, mapped)
      : null;
  }
}
