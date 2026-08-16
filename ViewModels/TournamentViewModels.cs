using System.ComponentModel.DataAnnotations;
using WebApplication1.Models;

namespace WebApplication1.ViewModels;

public class CreateTournamentViewModel
{
    [Required(ErrorMessage = "Tournament name is required.")]
    [StringLength(64, MinimumLength = 3, ErrorMessage = "Name must be 3-64 characters.")]
    public string Name { get; set; } = "";

    [Range(4, 6, ErrorMessage = "Player limit must be 4 or 6.")]
    public int PlayerLimit { get; set; } = 4;

    [Range(2, 5, ErrorMessage = "Rounds must be between 2 and 5.")]
    public int TotalRounds { get; set; } = 3;

    public int BaseMinutes { get; set; } = 10;

    public int IncrementSeconds { get; set; } = 0;
}

public class TournamentListItem
{
    public Tournament Tournament { get; set; } = new();

    public int PlayerCount { get; set; }
}

public class TournamentDetailViewModel
{
    public Tournament Tournament { get; set; } = new();

    public List<TournamentPlayer> Standings { get; set; } = new();

    public string? CurrentUserId { get; set; }

    public bool IsAdmin { get; set; }

    public bool CanJoin { get; set; }

    public bool IsRegistered { get; set; }
}

public class AdminViewModel
{
    public List<Tournament> Tournaments { get; set; } = new();

    public CreateTournamentViewModel Create { get; set; } = new();
}