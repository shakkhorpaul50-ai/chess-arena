using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.ViewModels;

namespace WebApplication1.Controllers;

public class TournamentController : Controller
{
    private readonly AppDbContext _db;

    public TournamentController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var tournaments = await _db.Tournaments
            .Include(t => t.Players)
            .OrderByDescending(t => t.CreatedUtc)
            .ToListAsync();
        var model = tournaments
            .Select(t => new TournamentListItem { Tournament = t, PlayerCount = t.Players.Count })
            .ToList();
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Detail(int id)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.Players)
                .ThenInclude(tp => tp.Player)
            .Include(t => t.Rounds)
                .ThenInclude(r => r.Matches)
                    .ThenInclude(m => m.WhitePlayer)
            .Include(t => t.Rounds)
                .ThenInclude(r => r.Matches)
                    .ThenInclude(m => m.BlackPlayer)
            .Include(t => t.Rounds)
                .ThenInclude(r => r.Matches)
                    .ThenInclude(m => m.Game)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tournament is null)
        {
            return NotFound();
        }

        var userId = User.Identity?.IsAuthenticated == true
            ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            : null;

        var standings = tournament.Players
            .OrderByDescending(p => p.Points)
            .ThenByDescending(p => p.Buchholz)
            .ThenByDescending(p => p.Player!.Rating)
            .ThenBy(p => p.Seed)
            .ToList();

        var model = new TournamentDetailViewModel
        {
            Tournament = tournament,
            Standings = standings,
            CurrentUserId = userId,
            IsAdmin = User.IsInRole("Admin"),
            CanJoin = userId is not null
                && tournament.Status == TournamentStatus.Registration
                && tournament.Players.Count < tournament.PlayerLimit
                && tournament.Players.All(p => p.PlayerId != userId),
            IsRegistered = userId is not null && tournament.Players.Any(p => p.PlayerId == userId)
        };
        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Join(int id)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null)
        {
            return Challenge();
        }

        var tournament = await _db.Tournaments
            .Include(t => t.Players)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tournament is null)
        {
            return NotFound();
        }
        if (tournament.Status != TournamentStatus.Registration)
        {
            return BadRequest("This tournament is not accepting players.");
        }
        if (tournament.Players.Count >= tournament.PlayerLimit)
        {
            return BadRequest("This tournament is full.");
        }
        if (tournament.Players.Any(p => p.PlayerId == userId))
        {
            return RedirectToAction(nameof(Detail), new { id });
        }

        _db.TournamentPlayers.Add(new TournamentPlayer
        {
            TournamentId = tournament.Id,
            PlayerId = userId,
            Seed = tournament.Players.Count + 1,
            JoinedUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Leave(int id)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId is null)
        {
            return Challenge();
        }
        var player = await _db.TournamentPlayers
            .FirstOrDefaultAsync(tp => tp.TournamentId == id && tp.PlayerId == userId);
        if (player is null)
        {
            return RedirectToAction(nameof(Detail), new { id });
        }
        var tournament = await _db.Tournaments.FindAsync(id);
        if (tournament is not null && tournament.Status != TournamentStatus.Registration)
        {
            return BadRequest("You can only leave a tournament during registration.");
        }
        _db.TournamentPlayers.Remove(player);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> DetailPartial(int id)
    {
        var tournament = await _db.Tournaments
            .Include(t => t.Players)
                .ThenInclude(tp => tp.Player)
            .Include(t => t.Rounds)
                .ThenInclude(r => r.Matches)
                    .ThenInclude(m => m.WhitePlayer)
            .Include(t => t.Rounds)
                .ThenInclude(r => r.Matches)
                    .ThenInclude(m => m.BlackPlayer)
            .Include(t => t.Rounds)
                .ThenInclude(r => r.Matches)
                    .ThenInclude(m => m.Game)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tournament is null)
        {
            return NotFound();
        }

        var userId = User.Identity?.IsAuthenticated == true
            ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            : null;

        var standings = tournament.Players
            .OrderByDescending(p => p.Points)
            .ThenByDescending(p => p.Buchholz)
            .ThenByDescending(p => p.Player!.Rating)
            .ThenBy(p => p.Seed)
            .ToList();

        var model = new TournamentDetailViewModel
        {
            Tournament = tournament,
            Standings = standings,
            CurrentUserId = userId,
            IsAdmin = User.IsInRole("Admin"),
            CanJoin = false,
            IsRegistered = userId is not null && tournament.Players.Any(p => p.PlayerId == userId)
        };
        return PartialView("_TournamentBoard", model);
    }
}