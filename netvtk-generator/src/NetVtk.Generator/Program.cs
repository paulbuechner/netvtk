// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;

// YamlDotNet
using YamlDotNet.Core;

//
using NetVtk.Generator;

const string Usage = """
  netvtk-gen <command> [options]

  --vtk <dir> is VTK's install prefix: include/vtk-X.Y, share/vtk (the module properties), bin (the DLLs, whose export
  tables tell which declared members exist; Windows).

  commands:
    dump      --vtk <dir> [--config config/modules.yaml] <module>
        what the parser sees in one module, parsed as generate parses it (development aid)
    generate  --vtk <dir> --core <netvtk-core> [--config config/modules.yaml] [--check] [names...]
        writes <netvtk-core>/src/SWIG_files/{wrapper,headers} for the named modules or libraries (default: the config's
        libraries) and src/SWIG_files/{modules,classes}.json; skipped members go to log/skips/<module>.txt. --check:
        write nothing, exit 1 if netvtk-core's files differ from the output. A module that doesn't parse fails the run,
        which then writes nothing.
    docs      --vtk <dir> --core <netvtk-core> --assembly <NetVtk.dll> --out <NetVtk.xml> [--xml <compiler's xml>] [--config config/modules.yaml]
        VTK's documentation comments as NetVtk's XML documentation file (IntelliSense), merged with the compiler's
        (--xml, the hand-written members').
  """;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
  Console.WriteLine(Usage);
  return 0;
}

var defaultConfig = Path.Combine("config", "modules.yaml");
try
{
  switch (args[0])
  {
    case "dump":
    {
      var line = CommandLine.Parse("dump", args[1..], ["--vtk", "--config"]);
      var module = line.Names is [var name]
        ? name
        : throw new ArgumentException("netvtk-gen dump: name one module");
      return Dump.Run(
        new Workspace(line.Option("--config") ?? defaultConfig, line.Required("--vtk")), module,
        Console.Out);
    }

    case "generate":
    {
      var line = CommandLine.Parse("generate", args[1..], ["--vtk", "--core", "--config"],
                                   ["--check"]);
      return Generate.Run(
        new Workspace(line.Option("--config") ?? defaultConfig, line.Required("--vtk")),
        line.Required("--core"), line.Names, Console.Out, line.Flag("--check"));
    }

    case "docs":
    {
      var line = CommandLine.Parse("docs", args[1..],
                                   ["--vtk", "--core", "--config", "--assembly", "--xml", "--out"]);
      return Docs.Run(
        new Workspace(line.Option("--config") ?? defaultConfig, line.Required("--vtk")),
        line.Required("--core"), line.Required("--assembly"), line.Option("--xml"),
        line.Required("--out"), Console.Out);
    }

    default:
      Console.Error.WriteLine($"netvtk-gen: unknown command '{args[0]}'");
      return 1;
  }
}
catch (Exception e) when (UserErrors(e) is { Count: > 0 } messages)
{
  foreach (var message in messages)
  {
    Console.Error.WriteLine(message);
  }

  return 2;
}

// the messages of errors a user can fix (arguments, configuration, input files); none for a bug,
// which keeps its stack trace
static List<string> UserErrors(Exception e) => e switch
{
  AggregateException aggregate when
    aggregate.InnerExceptions.Select(UserErrors).ToList() is var inner
    && inner.All(m => m.Count > 0) => [.. inner.SelectMany(m => m)],
  ArgumentException or InvalidOperationException or IOException or KeyNotFoundException
    or InvalidDataException or YamlException or JsonException or XmlException => [e.Message],
  _ => [],
};
