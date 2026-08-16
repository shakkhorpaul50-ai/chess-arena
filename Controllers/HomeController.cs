using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var liveGames = await _db.Games
            .Include(g => g.WhitePlayer)
            .Include(g => g.BlackPlayer)
            .Where(g => g.Status == GameStatus.Active || g.Status == GameStatus.Waiting)
            .OrderByDescending(g => g.CreatedUtc)
            .Take(12)
            .ToListAsync();

        var tournaments = await _db.Tournaments
            .Include(t => t.Players)
            .Where(t => t.Status == TournamentStatus.Registration || t.Status == TournamentStatus.Running)
            .OrderByDescending(t => t.CreatedUtc)
            .Take(6)
            .ToListAsync();

        ViewData["LiveGames"] = liveGames;
        ViewData["Tournaments"] = tournaments;
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}