using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Services;

public sealed class StatsService
{
    private readonly ILogger<StatsService> _logger;

    public StatsService(ILogger<StatsService> logger)
    {
        _logger = logger;
    }

    public async Task ApplyGameResultAsync(AppDbContext db, int gameId)
    {
        try
        {
            var game = await db.Games
                .Include(g => g.WhitePlayer)
                .Include(g => g.BlackPlayer)
                .FirstOrDefaultAsync(g => g.Id == gameId);
            if (game is null || game.Result is GameResult.Undecided or GameResult.Aborted)
            {
                return;
            }
            var white = game.WhitePlayer;
            var black = game.BlackPlayer;
            if (white is null || black is null)
            {
                return;
            }

            double whiteScore = game.Result switch
            {
                GameResult.WhiteWin => 1.0,
                GameResult.BlackWin => 0.0,
                _ => 0.5
            };
            double blackScore = 1.0 - whiteScore;

            ApplyToPlayer(white, whiteScore, black.Rating);
            ApplyToPlayer(black, blackScore, white.Rating);

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply stats for game {GameId}", gameId);
        }
    }

    private static void ApplyToPlayer(ApplicationUser player, double score, int opponentRating)
    {
        player.GamesPlayed++;
        if (score == 1.0)
        {
            player.Wins++;
        }
        else if (score == 0.0)
        {
            player.Losses++;
        }
        else
        {
            player.Draws++;
        }

        double expected = 1.0 / (1.0 + Math.Pow(10, (opponentRating - player.Rating) / 400.0));
        int k = player.Rating < 2100 ? 32 : 16;
        player.Rating = Math.Max(100, player.Rating + (int)Math.Round(k * (score - expected)));
    }
}