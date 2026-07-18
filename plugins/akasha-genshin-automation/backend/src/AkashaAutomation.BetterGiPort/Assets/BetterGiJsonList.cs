using System.Text.Json;

namespace AkashaAutomation.BetterGiPort.Assets;

public static class BetterGiJsonList
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static IReadOnlyList<string> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream, DocumentOptions);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"BetterGI list asset '{path}' must contain a JSON array.");
        }

        var values = new List<string>(document.RootElement.GetArrayLength());
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"BetterGI list asset '{path}' must contain strings only.");
            }

            values.Add(element.GetString()!);
        }

        return values;
    }
}
