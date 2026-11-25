// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

//
using NetVtk.Generator.Model;

namespace NetVtk.Generator.Emit;

/// <summary>
/// VTK's size hints that name a parameter (<c>VTK_SIZEHINT(pts, npts)</c>): it holds as many
/// elements as the count says, of the member's arguments (their values after the call), its
/// result (<c>_</c>) and numbers.
/// </summary>
internal static partial class SizeHints
{
  /// <summary>
  /// The parameters the hints of <paramref name="declarer"/> name, by position, each with its count
  /// as the member's SWIG wrapper spells it, or null for a count it can't spell (a call of another
  /// member): the wrapper's arguments are <c>arg1</c>, <c>arg2</c>, ..., the object first where
  /// there is one, a reference's value <c>(*argN)</c>, its result <c>result</c>.
  /// </summary>
  /// <param name="m">The member declared.</param>
  /// <param name="declarer">
  /// The member whose hints count: <paramref name="m"/>, or one it overrides
  /// (<see cref="Names"/>), whose parameters have the same types.
  /// </param>
  public static Dictionary<int, string?> Of(MethodModel m, MethodModel declarer)
  {
    Dictionary<int, string?> sized = [];
    foreach (var match in (declarer.Hints ?? []).Select(h => Named().Match(h))
             .Where(h => h.Success))
    {
      var index = IndexOf(declarer.Parameters, match.Groups[1].Value);
      if (index >= 0)
      {
        sized[index] = Count(match.Groups[2].Value, m, declarer.Parameters);
      }
    }

    return sized;
  }

  /// <summary>Whether a member has a hint that names a parameter.</summary>
  public static bool Names(MethodModel m) => (m.Hints ?? []).Any(h => Named().IsMatch(h));

  [GeneratedRegex(@"^vtk::sizehint\((\w+),\s*(.+)\)$")]
  private static partial Regex Named();

  [GeneratedRegex(@"\G\s*(?:(?<name>[A-Za-z_]\w*)|(?<number>\d+)|(?<operator>[-+*/()]))\s*")]
  private static partial Regex Token();

  private static int IndexOf(IReadOnlyList<ParameterModel> parameters, string name)
  {
    for (var i = 0; i < parameters.Count; i++)
    {
      if (parameters[i].Name == name)
      {
        return i;
      }
    }

    return -1;
  }

  // the count in the wrapper's terms, or null for one with a name it can't spell (Term)
  private static string? Count(string expression, MethodModel m,
                               IReadOnlyList<ParameterModel> parameters)
  {
    List<string> terms = [];
    var at = 0;
    foreach (Match token in Token().Matches(expression))
    {
      at += token.Length;
      if (!token.Groups["name"].Success)
      {
        terms.Add(token.Value.Trim());
        continue;
      }

      var term = Term(token.Groups["name"].Value, m, parameters);
      if (term is null)
      {
        return null;
      }

      terms.Add(term);
    }

    return at == expression.Length && terms.Count > 0 ? string.Join(" ", terms) : null;
  }

  // a name in the count, in the wrapper's terms: the result, or a number argument the wrapper
  // always has
  private static string? Term(string name, MethodModel m, IReadOnlyList<ParameterModel> parameters)
  {
    if (name == "_")
    {
      return m.Return is BuiltinType { Name: not "void" } ? "result" : null;
    }

    var index = IndexOf(parameters, name);
    if (index < 0 || parameters[index].Default is not null)
    {
      return null;
    }

    return parameters[index].Type switch
    {
      BuiltinType => $"arg{Argument(m, index)}",
      ReferenceType { Referee: BuiltinType } => $"(*arg{Argument(m, index)})",
      _ => null,
    };
  }

  // the wrapper's number of a parameter: SWIG's arguments count from 1, the object first
  private static int Argument(MethodModel m, int index) => index + (m.IsStatic ? 1 : 2);
}
