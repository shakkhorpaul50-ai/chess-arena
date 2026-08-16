using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers;

[Authorize]
public class BotController : Controller
{
    private readonly AppDbContext _db;

    public BotController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(string difficulty, int baseMinutes, int incrementSeconds)
    {
        if (string.IsNullOrWhiteSpace(difficulty))
        {
            difficulty = "medium";
        }
        difficulty = difficulty.ToLowerInvariant();
        if (difficulty is not ("easy" or "medium" or "hard"))
        {
            difficulty = "medium";
        }
        baseMinutes = Math.Clamp(baseMinutes, 1, 60);
        incrementSeconds = Math.Clamp(incrementSeconds, 0, 60);

        var me = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (me is null)
        {
            return Challenge();
        }

        var bot = await _db.Users.FirstOrDefaultAsync((Models.ApplicationUser u) => u.UserName == DbSeeder.BotUserName);
        if (bot is null)
        {
            return BadRequest("Bot is not available yet. Try again in a moment.");
        }

        var game = new Game
        {
            GameKey = Guid.NewGuid(),
            Mode = GameMode.Bot,
            Status = GameStatus.Waiting,
            WhitePlayerId = me,
            BlackPlayerId = bot.Id,
            BaseMinutes = baseMinutes,
            IncrementSeconds = incrementSeconds,
            WhiteMsLeft = baseMinutes * 60_000L,
            BlackMsLeft = baseMinutes * 60_000L,
            Difficulty = difficulty,
            CreatedUtc = DateTime.UtcNow
        };
        _db.Games.Add(game);
        await _db.SaveChangesAsync();

        return RedirectToAction("Play", "Game", new { key = game.GameKey });
    }
}