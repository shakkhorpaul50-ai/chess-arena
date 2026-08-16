using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.ViewModels;

namespace WebApplication1.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private readonly AppDbContext _db;

    public ProfileController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        var recentGames = await _db.Games
            .Include(g => g.WhitePlayer)
            .Include(g => g.BlackPlayer)
            .Where(g => (g.WhitePlayerId == userId || g.BlackPlayerId == userId) &&
                        g.Status == Models.GameStatus.Ended)
            .OrderByDescending(g => g.EndedUtc)
            .Take(20)
            .ToListAsync();

        var friendsCount = await _db.Friendships.CountAsync(f =>
            f.Status == Models.FriendshipStatus.Accepted &&
            (f.RequesterId == userId || f.AddresseeId == userId));

        return View(new ProfileViewModel
        {
            User = user,
            RecentGames = recentGames,
            FriendsCount = friendsCount
        });
    }
}