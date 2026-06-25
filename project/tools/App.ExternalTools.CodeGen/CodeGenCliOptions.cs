using System;

namespace App.ExternalTools.CodeGen;

public sealed record CodeGenCliOptions(string Tool, string SchemaDirectory, string OutputDirectory)
{
    public static CodeGenCliOptions Parse(IReadOnlyList<string> args)
    {
        var tool = ReadRequired(args, "--tool");
        var schemaDirectory = ReadRequired(args, "--schema-dir");
        var outputDirectory = ReadRequired(args, "--out-dir");

        return new CodeGenCliOptions(tool, schemaDirectory, outputDirectory);
    }

    private static string ReadRequired(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                throw new ArgumentException($"Missing value for {name}.");
            }

            return args[i + 1];
        }

        throw new ArgumentException($"Missing required argument {name}.");
    }
}
