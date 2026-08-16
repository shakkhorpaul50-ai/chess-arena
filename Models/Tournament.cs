namespace WebApplication1.Models;

public enum TournamentStatus
{
    Registration,
    Running,
    Completed,
    Cancelled
}

public class Tournament
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string CreatedById { get; set; } = "";

    public ApplicationUser? CreatedBy { get; set; }

    public int PlayerLimit { get; set; } = 4;

    public int TotalRounds { get; set; } = 3;

    public TournamentStatus Status { get; set; } = TournamentStatus.Registration;

    public int CurrentRound { get; set; }

    public int BaseMinutes { get; set; } = 10;

    public int IncrementSeconds { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? StartedUtc { get; set; }

    public DateTime? EndedUtc { get; set; }

    public List<TournamentPlayer> Players { get; set; } = new();

    public List<TournamentRound> Rounds { get; set; } = new();

    public string TimeControlLabel => $"{BaseMinutes}+{IncrementSeconds}";
}