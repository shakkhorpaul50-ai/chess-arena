namespace WebApplication1.Models;

public enum TournamentRoundStatus
{
    Pending,
    InProgress,
    Completed
}

public class TournamentRound
{
    public int Id { get; set; }

    public int TournamentId { get; set; }

    public Tournament? Tournament { get; set; }

    public int Number { get; set; }

    public TournamentRoundStatus Status { get; set; } = TournamentRoundStatus.InProgress;

    public List<TournamentMatch> Matches { get; set; } = new();
}