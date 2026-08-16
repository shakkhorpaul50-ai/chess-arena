using Microsoft.AspNetCore.Identity;

namespace WebApplication1.Models;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";

    public int Rating { get; set; } = 1200;

    public int GamesPlayed { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public int Draws { get; set; }

    public bool IsBot { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}