using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Services;
using WebApplication1.ViewModels;

namespace WebApplication1.Controllers;

[Authorize]
public class GameController : Controller
{
    private readonly AppDbContext _db;
    private readonly GameSessionManager _sessions;

    public GameController(AppDbContext db, GameSessionManager sessions)
    {
        _db = db;
        _sessions = sessions;
    }

    [HttpGet]
    public IActionResult Lobby()
    {
        var model = new LobbyViewModel
        {
            CurrentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "",
            TimeControls = new List<TimeControlOption>
            {
                new("Bullet 3+0", 3, 0),
                new("Bullet 3+2", 3, 2),
                new("Blitz 5+0", 5, 0),
                new("Blitz 5+3", 5, 3),
                new("Rapid 10+0", 10, 0),
                new("Rapid 10+5", 10, 5)
            }
        };
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Play(Guid key)
    {
        var game = await _db.Games
            .Include(g => g.WhitePlayer)
            .Include(g => g.BlackPlayer)
            .FirstOrDefaultAsync(g => g.GameKey == key);
        if (game is null)
        {
            return NotFound();
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        var snapshot = await _sessions.GetSnapshotForPageAsync(key, userId);
        if (snapshot is null)
        {
            return NotFound();
        }

        var model = new PlayViewModel
        {
            Game = game,
            CurrentUserId = userId,
            IsPlayer = snapshot.IsPlayer,
            IsSpectator = !snapshot.IsPlayer,
            IsWhite = snapshot.IsWhite
        };
        ViewData["Snapshot"] = snapshot;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Watch(Guid key)
    {
        var game = await _db.Games
            .Include(g => g.WhitePlayer)
            .Include(g => g.BlackPlayer)
            .FirstOrDefaultAsync(g => g.GameKey == key);
        if (game is null)
        {
            return NotFound();
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        var snapshot = await _sessions.GetSnapshotForPageAsync(key, userId);
        if (snapshot is null)
        {
            return NotFound();
        }

        var model = new PlayViewModel
        {
            Game = game,
            CurrentUserId = userId,
            IsPlayer = snapshot.IsPlayer,
            IsSpectator = !snapshot.IsPlayer,
            IsWhite = snapshot.IsWhite
        };
        ViewData["Snapshot"] = snapshot;
        return View(model);
    }
}