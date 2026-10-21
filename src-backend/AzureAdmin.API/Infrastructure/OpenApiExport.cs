using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace AzureAdmin.API.Infrastructure;

/// <summary>
/// Build-time export of the OpenAPI contract: <c>dotnet AzureAdmin.API.dll export-openapi &lt;file&gt;</c>.
/// Builds the app (so controllers, JSON options, and Swashbuckle configuration are exactly what runs in
/// production) but never starts it: no Kestrel, no migrations, no background services, no database needed.
/// The csproj runs this after every build and then regenerates the Angular client from the file.
/// </summary>
public static class OpenApiExport
{
    public const string Command = "export-openapi";
    public const string DocumentName = "v1";

    public static bool IsRequested(string[] args) =>
        args.Length >= 1 && string.Equals(args[0], Command, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args, WebApplication app)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            await Console.Error.WriteLineAsync($"Usage: {Command} <output-file>");
            return 2;
        }

        var outputPath = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger(DocumentName);
        var json = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0);

        // Only touch the file when the contract changed, so an unchanged build leaves git clean.
        var normalized = json.ReplaceLineEndings("\n");
        if (!normalized.EndsWith('\n'))
            normalized += "\n";
        if (File.Exists(outputPath) && File.ReadAllText(outputPath) == normalized)
        {
            Console.WriteLine($"OpenAPI contract unchanged: {outputPath}");
            return 0;
        }

        await File.WriteAllTextAsync(outputPath, normalized);
        Console.WriteLine($"OpenAPI contract written: {outputPath}");
        return 0;
    }
}
