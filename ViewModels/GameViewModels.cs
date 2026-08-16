using WebApplication1.Models;

namespace WebApplication1.ViewModels;

public record TimeControlOption(string Label, int BaseMinutes, int IncrementSeconds);

public class LobbyViewModel
{
    public List<TimeControlOption> TimeControls { get; set; } = new();

    public List<Game> ActiveGames { get; set; } = new();

    public string CurrentUserId { get; set; } = "";
}

public class PlayViewModel
{
    public Game? Game { get; set; }

    public string? CurrentUserId { get; set; }

    public bool IsPlayer { get; set; }

    public bool IsSpectator { get; set; }

    public bool IsWhite { get; set; }

    public bool IsBotGame => Game?.Mode == GameMode.Bot;

    public bool IsTournamentGame => Game?.Mode == GameMode.Tournament;
}