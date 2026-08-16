namespace WebApplication1.Models;

public class GameMove
{
    public int Id { get; set; }

    public int GameId { get; set; }

    public Game? Game { get; set; }

    public int MoveNumber { get; set; }

    public string San { get; set; } = "";

    public string From { get; set; } = "";

    public string To { get; set; } = "";

    public string? Promotion { get; set; }

    public string FenAfter { get; set; } = "";

    public long MsLeftAfter { get; set; }

    public bool IsWhite { get; set; }

    public DateTime PlayedAtUtc { get; set; } = DateTime.UtcNow;
}