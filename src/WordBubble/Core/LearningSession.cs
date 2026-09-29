namespace WordBubble;

// A short session is measured against official completion counts, never clicks.
public sealed class LearningSession
{
    private int? _baseline;
    private int? _total;
    public int Goal { get; private set; } = 3;
    public int Completed { get; private set; }
    public bool Active { get; private set; }
    public bool Finished { get; private set; }
    public bool HasProgress => _baseline.HasValue;
    public void Start(int goal, string progress)
    {
        Goal = goal is 3 or 5 ? goal : 0;
        Completed = 0; Finished = false; Active = true; _baseline = null; _total = null;
        Observe(progress);
    }
    public void Observe(string progress)
    {
        if (!Active || !TryProgress(progress, out var finished, out var total)) return;
        if (!_baseline.HasValue || _total != total || finished < _baseline)
        { _baseline = finished; _total = total; Completed = 0; return; }
        Completed = Math.Max(0, finished - _baseline.Value);
        if (Goal > 0 && Completed >= Goal) { Active = false; Finished = true; }
    }
    public void FinishRound() { Active = false; Finished = true; }
    public void Stop() { Active = false; Finished = false; }
    public static bool TryProgress(string? value, out int finished, out int total)
    {
        finished = total = 0;
        var pieces = (value ?? "").Split('/');
        return pieces.Length == 2 && int.TryParse(pieces[0].Trim(), out finished) &&
            int.TryParse(pieces[1].Trim(), out total) && finished >= 0 && total >= finished && total <= 1000000;
    }
}
