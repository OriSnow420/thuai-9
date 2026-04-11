namespace Thuai.GameLogic;

public class News
{
    public int NewsId { get; init; }
    public int PublishTick { get; init; }
    public string Content { get; init; } = "";
    public NewsSentiment Sentiment { get; init; }
    public bool IsFake { get; init; }
    public string? SourcePlayer { get; init; }
}
