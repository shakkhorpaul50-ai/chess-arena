namespace WebApplication1.Models;

public class TournamentPlayer
{
    public int Id { get; set; }

    public int TournamentId { get; set; }

    public Tournament? Tournament { get; set; }

    public string PlayerId { get; set; } = "";

    public ApplicationUser? Player { get; set; }

    public int Seed { get; set; }

    public int Points { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public int Draws { get; set; }

    public int Buchholz { get; set; }

    public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;
}