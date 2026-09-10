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
            await using var engine = new OcrEngine();
            await engine.InitializeAsync();
            var response = await engine.RecognizeAsync(
                new OcrRequest(Guid.NewGuid().ToString("N"), args[imageOption + 1]),
                CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(response));
            return response.Success ? 0 : 1;
        }

        return await OcrPipeServer.RunAsync(CancellationToken.None);
    }
}
