using System.Text.Json;

namespace WordBubble;

public sealed class StudyAction
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Hint { get; set; } = "";
}

public sealed class StudyExample
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class StudySnapshot
{
    public string Stage { get; set; } = "page";
    public string Stamp { get; set; } = "";
    public string Word { get; set; } = "";
    public string Phonetic { get; set; } = "";
    public string Meaning { get; set; } = "";
    // Translations are intentionally absent: fetched separately after a click.
    public StudyExample[] Examples { get; set; } = [];
    public string Progress { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Waiting { get; set; }
    public StudyAction[] Actions { get; set; } = [];
    public bool IsCard => Stage is "recall" or "answer" or "spelling" or "spelling-recall";
    public bool Can(string action) => Actions.Any(a => a.Id == action);

    public static StudySnapshot Page(string message) => new() { Message = message };

    public static StudySnapshot Parse(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 128000) return Page("学习内容暂不可用，请重新连接。");
        try
        {
            var value = JsonSerializer.Deserialize<StudySnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (value is null || value.Stage is not ("recall" or "answer" or "spelling" or "spelling-recall" or "login" or "page" or "complete" or "loading" or "blocked" or "ready"))
                return Page("当前页面暂不支持迷你卡片");
            value.Word = Limit(value.Word, 120);
            value.Phonetic = Limit(value.Phonetic, 180);
            value.Meaning = Limit(value.Meaning, 1200);
            value.Examples = (value.Examples ?? []).Where(example => example != null &&
                !string.IsNullOrWhiteSpace(example.Id) && example.Id.Length <= 100 && !string.IsNullOrWhiteSpace(example.Text))
                .DistinctBy(example => example.Id).ToArray();
            foreach (var example in value.Examples) example.Text = Limit(example.Text, 700);
            value.Progress = Limit(value.Progress, 40);
            value.Message = Limit(value.Message, 1200);
            value.Stamp = Limit(value.Stamp, 100);
            value.Actions = (value.Actions ?? []).Where(a => a != null && IsAllowedAction(a.Id)).Take(8).ToArray();
            foreach (var action in value.Actions) { action.Label = Limit(action.Label, 20); action.Hint = Limit(action.Hint, 40); }
            if (value.IsCard && (value.Stamp.Length == 0 || (value.Stage is "recall" or "answer" && value.Word.Length == 0))) return Page("正在等待可用的单词内容，请稍后重试连接");
            return value;
        }
        catch (JsonException) { return Page("正在等待学习页面"); }
    }
    public static bool IsAllowedAction(string? id) => id is "reveal" or "audio" or "familiar" or "vague" or "forget" or
        "login" or "account-continue" or "entry-login" or "entry-retry" or "entry-consent" or "entry-start" or "spelling-start" or "spelling-input" or "continue" or "sign-in" or "review-tab" or "dialog-0" or "dialog-1" or "dialog-2" or "dialog-3" or "native-dialog-ok" or "native-dialog-cancel";
    private static string Limit(string? value, int maximum) => value is null ? "" : value[..Math.Min(value.Length, maximum)];
}
