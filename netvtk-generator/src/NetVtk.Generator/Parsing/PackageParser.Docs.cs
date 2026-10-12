// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// ClangSharp
using ClangSharp;
using ClangSharp.Interop;

namespace NetVtk.Generator.Parsing;

/// <summary>
/// A documented member: its name (<c>#ctor</c> for a constructor), its parameters' names, its XML
/// documentation.
/// </summary>
internal sealed record MemberDocs(string Name, IReadOnlyList<string> Parameters, string Xml);

/// <summary>A type's documentation: its own, and its documented members'.</summary>
internal sealed class TypeDocs
{
  public string? Xml { get; set; }

  public List<MemberDocs> Members { get; } = [];
}

// the part of PackageParser that reads VTK's documentation comments (netvtk-gen docs)
internal sealed partial class PackageParser
{
  /// <summary>
  /// The documentation comments in a package's headers, by type: the C++ name without template
  /// arguments (the name VTK's reference documents), a namespace for its functions, a typedef's own
  /// name. A parse of its own, without function bodies; a header whose error is fatal (a missing
  /// include, which hides the headers after it) is left out, and a package none of whose headers
  /// compiles fails, as for the generator.
  /// </summary>
  public Dictionary<string, TypeDocs> ParseDocs(string package, IReadOnlyList<string> headers,
                                                IReadOnlyList<string> prelude)
  {
    List<string> own = [.. headers];
    (string Header, string Reason)? first = null;
    while (own.Count > 0)
    {
      var fileName = $"netvtk_docs_{package}.cpp";
      List<string> included = [.. prelude, .. own];
      using var index = CXIndex.Create();
      using var unsaved =
        CXUnsavedFile.Create(
          fileName, string.Concat(included.Select(header => $"#include <{header}>\n")));
      var error = CXTranslationUnit.TryParse(index, fileName, Arguments(), [unsaved],
                                             CXTranslationUnit_Flags
                                               .CXTranslationUnit_IncludeAttributedTypes
                                             | CXTranslationUnit_Flags
                                               .CXTranslationUnit_DetailedPreprocessingRecord
                                             | CXTranslationUnit_Flags
                                               .CXTranslationUnit_SkipFunctionBodies,
                                             out var handle);
      if (error != CXErrorCode.CXError_Success)
      {
        throw new InvalidOperationException($"{package}: libclang failed to parse ({error})");
      }

      using var unit = TranslationUnit.GetOrCreate(handle);
      var fatal = FatalError(package, handle, Errors(handle), fileName, included, own);
      if (fatal is var (culprit, reason))
      {
        first ??= (culprit, reason);
        own.Remove(culprit);
        continue;
      }

      Dictionary<string, TypeDocs> docs = new(StringComparer.Ordinal);
      CollectDocs(unit.TranslationUnitDecl.Decls,
                  new HashSet<string>(own, StringComparer.OrdinalIgnoreCase), docs, new Sources());
      ClassBlocks(package, own, docs);
      return docs;
    }

    if (first is var (header, why))
    {
      throw new InvalidOperationException($"{package}: no header compiles, e.g. {header}: {why}");
    }

    return [];
  }

  // vtkSOADataArrayTemplate<float>::StorageTypeEnum as vtkSOADataArrayTemplate::StorageTypeEnum
  internal static string WithoutTemplateArguments(string spelling)
  {
    var text = new StringBuilder();
    var depth = 0;
    foreach (var c in spelling)
    {
      depth += c switch { '<' => 1, '>' => -1, _ => 0 };
      if (depth == 0 && c != '>')
      {
        text.Append(c);
      }
    }

    return text.ToString();
  }

  /// <summary>
  /// The class comments VTK writes at the top of a header (<c>/** @class vtkPolyData @brief ...
  /// */</c>, before the include guard), which libclang attaches to nothing: each parsed again as
  /// the comment of a stand-in declaration, for the classes no attached comment documents.
  /// </summary>
  private void ClassBlocks(string package, IReadOnlyList<string> headers,
                           Dictionary<string, TypeDocs> docs)
  {
    List<string> names = [];
    var text = new StringBuilder();
    foreach (var header in headers)
    {
      var path = includeDirectories.Select(d => Path.Combine(d, header))
        .FirstOrDefault(File.Exists);
      if (path is null)
      {
        continue;
      }

      foreach (Match block in ClassBlockPattern().Matches(File.ReadAllText(path)))
      {
        var name = block.Groups[1].Value;
        if (docs.TryGetValue(name, out var type) && type.Xml is not null)
        {
          continue;
        }

        text.Append(block.Value).Append($"\nstruct NetVtk_Doc_{names.Count} {{}};\n");
        names.Add(name);
      }
    }

    if (names.Count == 0)
    {
      return;
    }

    var fileName = $"netvtk_classdocs_{package}.cpp";
    using var index = CXIndex.Create();
    using var unsaved = CXUnsavedFile.Create(fileName, text.ToString());
    if (CXTranslationUnit.TryParse(index, fileName, Arguments(), [unsaved],
                                   CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies,
                                   out var handle)
        != CXErrorCode.CXError_Success)
    {
      return;
    }

    using var unit = TranslationUnit.GetOrCreate(handle);
    foreach (var record in unit.TranslationUnitDecl.Decls.OfType<CXXRecordDecl>())
    {
      if (record.Name.StartsWith("NetVtk_Doc_", StringComparison.Ordinal)
          && int.TryParse(record.Name["NetVtk_Doc_".Length..], out var i)
          && DocComments.ToXml(record.Handle.ParsedComment) is { } xml)
      {
        Of(docs, names[i]).Xml ??= xml;
      }
    }
  }

  // a /** */ block that names a class with @class (or \class)
  [GeneratedRegex(@"/\*\*(?:(?!\*/)[\s\S])*?[@\\]class\s+(\w+)(?:(?!\*/)[\s\S])*\*/")]
  private static partial Regex ClassBlockPattern();

  /// <summary>The headers' text, read once: member groups are told from it.</summary>
  private sealed class Sources
  {
    private readonly Dictionary<string, string?> _texts = new(StringComparer.OrdinalIgnoreCase);

    public string? Text(string path)
    {
      if (!_texts.TryGetValue(path, out var text))
      {
        _texts[path] = text = File.Exists(path) ? File.ReadAllText(path) : null;
      }

      return text;
    }
  }

  // the documented declarations of the package's own headers
  private static void CollectDocs(IEnumerable<Decl> decls, HashSet<string> headers,
                                  Dictionary<string, TypeDocs> docs, Sources sources)
  {
    foreach (var decl in decls)
    {
      switch (decl)
      {
        case NamespaceDecl scope:
          CollectDocs(scope.Decls, headers, docs, sources);
          break;
        case LinkageSpecDecl linkage:
          CollectDocs(linkage.Decls, headers, docs, sources);
          break;
        case ClassTemplateDecl
        {
          TemplatedDecl: CXXRecordDecl { IsThisDeclarationADefinition: true } pattern
        } template when IsIn(template, headers):
          RecordDocs(pattern, template.Handle.ParsedComment, docs, sources);
          break;
        case CXXRecordDecl { IsThisDeclarationADefinition: true } record
          when record is not ClassTemplateSpecializationDecl && IsIn(record, headers):
          RecordDocs(record, record.Handle.ParsedComment, docs, sources);
          break;
        case EnumDecl e when IsIn(e, headers):
          EnumDocs(e, docs);
          break;
        case FunctionDecl { DeclContext: NamespaceDecl scope } function when IsIn(function, headers)
          && DocComments.ToXml(function.Handle.ParsedComment) is { } xml:
          Of(docs, scope.QualifiedName)
            .Members.Add(new MemberDocs(function.Name, ParameterNames(function), xml));
          break;
        // an alias's own comment (one that names an instance): the type C# names after it
        case TypedefNameDecl { DeclContext: TranslationUnitDecl } typedef
          when IsIn(typedef, headers) && DocComments.ToXml(typedef.Handle.ParsedComment) is { } xml:
          Of(docs, typedef.Name).Xml ??= xml;
          break;
      }
    }
  }

  // a class, its public members, and the classes and enums inside it. VTK documents a group of
  // members once (///@{, a comment, the members, ///@}): libclang attaches the comment to the
  // first, the others get it from the group, told by the markers in the text between declarations.
  private static void RecordDocs(CXXRecordDecl record, CXComment comment,
                                 Dictionary<string, TypeDocs> docs, Sources sources)
  {
    var type = Of(docs, WithoutTemplateArguments(record.QualifiedName));
    type.Xml ??= DocComments.ToXml(comment);
    string? group = null;
    var inGroup = false;
    uint previousEnd = 0;
    foreach (var decl in record.Decls)
    {
      decl.Extent.Start.GetFileLocation(out var file, out _, out _, out var start);
      decl.Extent.End.GetFileLocation(out _, out _, out _, out var end);
      if (file.Handle != IntPtr.Zero
          && sources.Text(Consume(file.Name)) is { } text
          && start >= previousEnd
          && start <= text.Length)
      {
        var gap = text[(int)previousEnd..(int)start];
        var open = gap.LastIndexOf("@{", StringComparison.Ordinal);
        var close = gap.LastIndexOf("@}", StringComparison.Ordinal);
        if (open > close)
        {
          (inGroup, group) = (true, null);
        }
        else if (close >= 0)
        {
          (inGroup, group) = (false, null);
        }

        previousEnd = Math.Max(previousEnd, end);
      }

      if (decl.Access is not (CX_CXXAccessSpecifier.CX_CXXPublic
          or CX_CXXAccessSpecifier.CX_CXXInvalidAccessSpecifier))
      {
        continue;
      }

      switch (decl)
      {
        case CXXMethodDecl method when Grouped(method.Handle.ParsedComment) is { } xml:
          type.Members.Add(new MemberDocs(method is CXXConstructorDecl ? "#ctor" : method.Name,
                                          ParameterNames(method), xml));
          break;
        case FieldDecl field when Grouped(field.Handle.ParsedComment) is { } xml:
          type.Members.Add(new MemberDocs(field.Name, [], xml));
          break;
        case ClassTemplateDecl
        {
          TemplatedDecl: CXXRecordDecl { IsThisDeclarationADefinition: true } pattern
        } template:
          RecordDocs(pattern, template.Handle.ParsedComment, docs, sources);
          break;
        case CXXRecordDecl { IsThisDeclarationADefinition: true } nested
          when nested is not ClassTemplateSpecializationDecl && nested.Name.Length > 0:
          RecordDocs(nested, nested.Handle.ParsedComment, docs, sources);
          break;
        case EnumDecl e:
          EnumDocs(e, docs);
          break;
      }
    }

    // a member's own comment, or its group's: the group's first
    string? Grouped(CXComment own)
    {
      var xml = DocComments.ToXml(own);
      if (!inGroup)
      {
        return xml;
      }

      group ??= xml;
      return xml ?? group;
    }
  }

  private static void EnumDocs(EnumDecl e, Dictionary<string, TypeDocs> docs)
  {
    if (e.Name.Length == 0)
    {
      return;
    }

    var type = Of(docs, WithoutTemplateArguments(e.QualifiedName));
    type.Xml ??= DocComments.ToXml(e.Handle.ParsedComment);
    foreach (var constant in e.Enumerators)
    {
      if (DocComments.ToXml(constant.Handle.ParsedComment) is { } xml)
      {
        type.Members.Add(new MemberDocs(constant.Name, [], xml));
      }
    }
  }

  private static TypeDocs Of(Dictionary<string, TypeDocs> docs, string name)
  {
    if (!docs.TryGetValue(name, out var type))
    {
      docs[name] = type = new TypeDocs();
    }

    return type;
  }

  // declared in one of these headers (a macro expansion counts where it is written)
  private static bool IsIn(Decl decl, HashSet<string> headers)
  {
    decl.Location.GetFileLocation(out var file, out _, out _, out _);
    return headers.Contains(Path.GetFileName(Consume(file.Name)));
  }
}
