using Microsoft.AspNetCore.SignalR;
using WebApplication1.Hubs;

namespace WebApplication1.Services;

public sealed class BotService
{
    private readonly StockfishClient _stockfish;
    private readonly GameSessionManager _sessions;
    private readonly ILogger<BotService> _logger;

    public BotService(StockfishClient stockfish, GameSessionManager sessions, ILogger<BotService> logger)
    {
        _stockfish = stockfish;
        _sessions = sessions;
        _logger = logger;
    }

    public void QueueBotMove(Guid gameKey, string difficulty)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var (skill, moveTimeMs) = difficulty.ToLowerInvariant() switch
                {
                    "easy" => (1, 300),
                    "hard" => (20, 1200),
                    _ => (8, 600)
                };

                var thinkDelay = difficulty.ToLowerInvariant() == "hard"
                    ? Random.Shared.Next(700, 1400)
                    : Random.Shared.Next(300, 900);
                await Task.Delay(thinkDelay);

                var fen = _sessions.GetFenForBot(gameKey);
                if (fen is null)
                {
                    return;
                }

                string? best = null;
                if (difficulty.ToLowerInvariant() != "easy" || Random.Shared.Next(100) >= 40)
                {
                    best = _stockfish.GetBestMove(fen, skill, moveTimeMs);
                }
                best ??= _sessions.GetRandomLegalMove(gameKey);
                if (string.IsNullOrEmpty(best) || best.Length < 4)
                {
                    return;
                }

                var from = best.Substring(0, 2);
                var to = best.Substring(2, 2);
                char? promotion = best.Length > 4 ? char.ToUpper(best[4]) : null;

                await _sessions.ApplyBotMoveAsync(gameKey, from, to, promotion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bot move failed for game {GameKey}", gameKey);
            }
        });
    }
}