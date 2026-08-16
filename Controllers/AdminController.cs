using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Services;
using WebApplication1.ViewModels;

namespace WebApplication1.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly TournamentService _tournaments;

    public AdminController(AppDbContext db, TournamentService tournaments)
    {
        _db = db;
        _tournaments = tournaments;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var tournaments = await _db.Tournaments
            .Include(t => t.Players)
            .OrderByDescending(t => t.CreatedUtc)
            .ToListAsync();
        var model = new AdminViewModel
        {
            Tournaments = tournaments,
            Create = new CreateTournamentViewModel()
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateTournamentViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var tournaments = await _db.Tournaments
                .Include(t => t.Players)
                .OrderByDescending(t => t.CreatedUtc)
                .ToListAsync();
            return View("Index", new AdminViewModel { Tournaments = tournaments, Create = model });
        }

        var adminId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        _db.Tournaments.Add(new Models.Tournament
        {
            Name = model.Name.Trim(),
            CreatedById = adminId,
            PlayerLimit = model.PlayerLimit,
            TotalRounds = model.TotalRounds,
            BaseMinutes = model.BaseMinutes,
            IncrementSeconds = model.IncrementSeconds,
            CreatedUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id)
    {
        try
        {
            await _tournaments.StartAsync(_db, id);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        await _tournaments.CancelAsync(_db, id);
        return RedirectToAction(nameof(Index));
    }
}