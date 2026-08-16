namespace WebApplication1.Models;

public enum GameMode
{
    PvP,
    Bot,
    Tournament
}

public enum GameStatus
{
    Waiting,
    Active,
    Ended,
    Aborted
}

public enum GameResult
{
    Undecided,
    WhiteWin,
    BlackWin,
    Draw,
    Aborted
}

public class Game
{
    public int Id { get; set; }

    public Guid GameKey { get; set; } = Guid.NewGuid();

    public GameMode Mode { get; set; }

    public GameStatus Status { get; set; } = GameStatus.Waiting;

    public GameResult Result { get; set; } = GameResult.Undecided;

    public string? WhitePlayerId { get; set; }

    public ApplicationUser? WhitePlayer { get; set; }

    public string? BlackPlayerId { get; set; }

    public ApplicationUser? BlackPlayer { get; set; }

    public string? WinnerUserId { get; set; }

    public int BaseMinutes { get; set; } = 10;

    public int IncrementSeconds { get; set; }

    public long WhiteMsLeft { get; set; }

    public long BlackMsLeft { get; set; }

    public string Fen { get; set; } = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public string Pgn { get; set; } = "";

    public string? Difficulty { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? StartedUtc { get; set; }

    public DateTime? EndedUtc { get; set; }

    public DateTime? LastMoveUtc { get; set; }

    public int? TournamentMatchId { get; set; }

    public TournamentMatch? TournamentMatch { get; set; }

    public List<GameMove> Moves { get; set; } = new();

    public string TimeControlLabel => $"{BaseMinutes}+{IncrementSeconds}";
}