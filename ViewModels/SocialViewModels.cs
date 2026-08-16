using WebApplication1.Models;

namespace WebApplication1.ViewModels;

public class ProfileViewModel
{
    public ApplicationUser User { get; set; } = new();

    public List<Game> RecentGames { get; set; } = new();

    public int FriendsCount { get; set; }
}

public class FriendsViewModel
{
    public List<(ApplicationUser Friend, int Wins)> Friends { get; set; } = new();

    public List<Friendship> IncomingRequests { get; set; } = new();

    public List<Friendship> OutgoingRequests { get; set; } = new();

    public string? AddByEmail { get; set; }
}