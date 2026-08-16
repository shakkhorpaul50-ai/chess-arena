namespace WebApplication1.Models;

public class TournamentMatch
{
    public int Id { get; set; }

    public int RoundId { get; set; }

    public TournamentRound? Round { get; set; }

    public int TournamentId { get; set; }

    public Tournament? Tournament { get; set; }

    public string WhitePlayerId { get; set; } = "";

    public ApplicationUser? WhitePlayer { get; set; }

    public string BlackPlayerId { get; set; } = "";

    public ApplicationUser? BlackPlayer { get; set; }

    public int? GameId { get; set; }

    public Game? Game { get; set; }

    public GameResult Result { get; set; } = GameResult.Undecided;

    public int WhitePoints { get; set; }

    public int BlackPoints { get; set; }
}