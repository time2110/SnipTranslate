using System.Text.Json;
using RapidOCRLib;

namespace SnipTranslate.OcrWorker;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--inspect-api", StringComparer.OrdinalIgnoreCase))
        {
            foreach (var type in typeof(OcrLite).Assembly.GetTypes().Where(type =>
                         type.Name.Contains("Result", StringComparison.OrdinalIgnoreCase) ||
                         type.Name.Contains("Block", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine(type.FullName);
                foreach (var property in type.GetProperties())
                {
                    Console.WriteLine($"  property {property.PropertyType.Name} {property.Name}");
                }
                foreach (var field in type.GetFields())
                {
                    Console.WriteLine($"  field {field.FieldType.Name} {field.Name}");
                }
            }
            return 0;
        }

        if (args.Contains("--health", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "ok",
                modelsPresent = OcrEngine.ModelsPresent()
            }));
            return 0;
        }

        var imageOption = Array.FindIndex(args, value =>
            value.Equals("--image", StringComparison.OrdinalIgnoreCase));
        if (imageOption >= 0 && imageOption + 1 < args.Length)
        {
            var languageOption = Array.FindIndex(args, value =>
                value.Equals("--language", StringComparison.OrdinalIgnoreCase));
            var language = languageOption >= 0 && languageOption + 1 < args.Length ? args[languageOption + 1] : "auto";
            var qualityOption = Array.FindIndex(args, value =>
                value.Equals("--quality", StringComparison.OrdinalIgnoreCase));
            var quality = qualityOption >= 0 && qualityOption + 1 < args.Length ? args[qualityOption + 1] : "fast";
            var enhanced = args.Contains("--enhanced", StringComparer.OrdinalIgnoreCase);
            await using var engine = new OcrEngine();
            await engine.InitializeAsync();
            var response = await engine.RecognizeAsync(
                new OcrRequest(Guid.NewGuid().ToString("N"), args[imageOption + 1], language, true, quality, enhanced),
                CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(response));
            return response.Success ? 0 : 1;
        }

        return await OcrPipeServer.RunAsync(CancellationToken.None);
    }
}
