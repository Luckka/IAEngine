using System.Text.Json;

namespace OnlineOs.AiOrchestrator.Infrastructure;

public static class JsonSupport
{
    public static T? DeserializeObject<T>(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(text[start..(end + 1)], new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
        }
        catch (JsonException) { return default; }
    }
}
