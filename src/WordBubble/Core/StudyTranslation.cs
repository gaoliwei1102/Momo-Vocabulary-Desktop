using System.Text.Json;

namespace WordBubble;

// Independent read result. It never authorizes or counts a learning action.
public sealed class StudyTranslation
{
    public string Stamp { get; set; } = "";
    public string Id { get; set; } = "";
    public string? Text { get; set; }

    public static string? Parse(string? json, string stamp, string exampleId)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 8000) return null;
        try
        {
            var value = JsonSerializer.Deserialize<StudyTranslation>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (value == null || value.Stamp != stamp || value.Id != exampleId || value.Text == null || value.Text.Length > 700) return null;
            return value.Text;
        }
        catch (JsonException) { return null; }
    }
}
