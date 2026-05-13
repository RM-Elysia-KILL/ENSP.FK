namespace ENSP.FK.Models;

public class ChatMessage
{
    public string Role { get; init; } = string.Empty; // "user", "ai", "system", "status"
    public string Content { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public bool IsUser => Role == "user";
    public bool IsAi => Role == "ai";
    public bool IsStatus => Role == "status";
}
