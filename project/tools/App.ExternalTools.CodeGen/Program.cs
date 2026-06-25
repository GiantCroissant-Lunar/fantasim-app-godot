using System;

namespace App.ExternalTools.CodeGen;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = CodeGenCliOptions.Parse(args);
            if (!string.Equals(options.Tool, "vplanet", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Unsupported external tool '{options.Tool}'. Expected 'vplanet'.");
                return 2;
            }

            var result = new VplanetDtoGenerator().Generate(options.SchemaDirectory, options.OutputDirectory);
            Console.WriteLine(result.OutputPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
