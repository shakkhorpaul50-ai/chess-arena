using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Hubs;
using WebApplication1.Models;

namespace WebApplication1.Services;

public sealed class TournamentService
{
    private readonly ILogger<TournamentService> _logger;
    private readonly IHubContext<GameHub> _hub;

    public TournamentService(ILogger<TournamentService> logger, IHubContext<GameHub> hub)
    {
        _logger = logger;
        _hub = hub;
    }

    public async Task StartAsync(AppDbContext db, int tournamentId)
    {
        var tournament = await db.Tournaments
            .Include(t => t.Players)
            .FirstOrDefaultAsync(t => t.Id == tournamentId);
        if (tournament is null || tournament.Status != TournamentStatus.Registration)
        {
            return;
        }
        if (tournament.Players.Count != tournament.PlayerLimit)
        {
            throw new InvalidOperationException($"Tournament needs exactly {tournament.PlayerLimit} players to start.");
        }

        tournament.Status = TournamentStatus.Running;
        tournament.StartedUtc = DateTime.UtcNow;
        tournament.CurrentRound = 1;
        await db.SaveChangesAsync();

        await CreateRoundAsync(db, tournament, 1);
    }

    public async Task CancelAsync(AppDbContext db, int tournamentId)
    {
        var tournament = await db.Tournaments.FindAsync(tournamentId);
        if (tournament is null || tournament.Status is TournamentStatus.Completed or TournamentStatus.Cancelled)
        {
            return;
        }
        tournament.Status = TournamentStatus.Cancelled;
        tournament.EndedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task CompleteMatchAsync(AppDbContext db, int matchId)
    {
        var match = await db.TournamentMatches
            .Include(m => m.Game)
            .FirstOrDefaultAsync(m => m.Id == matchId);
        if (match is null || match.Result != GameResult.Undecided)
        {
            return;
        }
        var game = match.Game;
        if (game is null || game.Result is GameResult.Undecided or GameResult.Aborted)
        {
            return;
        }

        match.Result = game.Result;
        (match.WhitePoints, match.BlackPoints) = game.Result switch
        {
            GameResult.WhiteWin => (3, 0),
            GameResult.BlackWin => (0, 3),
            _ => (1, 1)
        };

        var whiteTp = await db.TournamentPlayers
            .FirstOrDefaultAsync(tp => tp.TournamentId == match.TournamentId && tp.PlayerId == match.WhitePlayerId);
        var blackTp = await db.TournamentPlayers
            .FirstOrDefaultAsync(tp => tp.TournamentId == match.TournamentId && tp.PlayerId == match.BlackPlayerId);
        if (whiteTp is not null)
        {
            ApplyResult(whiteTp, match.WhitePoints);
        }
        if (blackTp is not null)
        {
            ApplyResult(blackTp, match.BlackPoints);
        }

        await db.SaveChangesAsync();
        await RecomputeBuchholzAsync(db, match.TournamentId);
        await AdvanceIfRoundCompleteAsync(db, match.TournamentId);
        await _hub.Clients.Group($"tournament:{match.TournamentId}").SendAsync("TournamentUpdate", match.TournamentId);
    }

    private static void ApplyResult(TournamentPlayer tp, int points)
    {
        tp.Points += points;
        switch (points)
        {
            case 3:
                tp.Wins++;
                break;
            case 1:
                tp.Draws++;
                break;
            default:
                tp.Losses++;
                break;
        }
    }

    public async Task RecomputeBuchholzAsync(AppDbContext db, int tournamentId)
    {
        var players = await db.TournamentPlayers
            .Where(tp => tp.TournamentId == tournamentId)
            .ToListAsync();
        var matches = await db.TournamentMatches
            .Where(m => m.TournamentId == tournamentId && m.Result != GameResult.Undecided)
            .ToListAsync();

        foreach (var tp in players)
        {
            int buchholz = 0;
            foreach (var m in matches)
            {
                if (m.WhitePlayerId == tp.PlayerId)
                {
                    var opp = players.FirstOrDefault(p => p.PlayerId == m.BlackPlayerId);
                    if (opp is not null)
                    {
                        buchholz += opp.Points;
                    }
                }
                else if (m.BlackPlayerId == tp.PlayerId)
                {
                    var opp = players.FirstOrDefault(p => p.PlayerId == m.WhitePlayerId);
                    if (opp is not null)
                    {
                        buchholz += opp.Points;
                    }
                }
            }
            tp.Buchholz = buchholz;
        }
        await db.SaveChangesAsync();
    }

    private async Task AdvanceIfRoundCompleteAsync(AppDbContext db, int tournamentId)
    {
        var tournament = await db.Tournaments
            .Include(t => t.Rounds)
                .ThenInclude(r => r.Matches)
            .FirstOrDefaultAsync(t => t.Id == tournamentId);
        if (tournament is null || tournament.Status != TournamentStatus.Running)
        {
            return;
        }

        var currentRound = tournament.Rounds.FirstOrDefault(r => r.Number == tournament.CurrentRound);
        if (currentRound is null || currentRound.Matches.Any(m => m.Result == GameResult.Undecided))
        {
            return;
        }

        currentRound.Status = TournamentRoundStatus.Completed;

        if (tournament.CurrentRound >= tournament.TotalRounds)
        {
            tournament.Status = TournamentStatus.Completed;
            tournament.EndedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return;
        }

        tournament.CurrentRound++;
        await db.SaveChangesAsync();
        await CreateRoundAsync(db, tournament, tournament.CurrentRound);
    }

    private async Task CreateRoundAsync(AppDbContext db, Tournament tournament, int roundNumber)
    {
        var players = await db.TournamentPlayers
            .Where(tp => tp.TournamentId == tournament.Id)
            .Include(tp => tp.Player)
            .ToListAsync();
        var allMatches = await db.TournamentMatches
            .Where(m => m.TournamentId == tournament.Id)
            .ToListAsync();

        var ranked = players
            .OrderByDescending(p => p.Points)
            .ThenByDescending(p => p.Buchholz)
            .ThenByDescending(p => p.Player!.Rating)
            .ThenBy(p => p.Seed)
            .ToList();

        var round = new TournamentRound
        {
            TournamentId = tournament.Id,
            Number = roundNumber,
            Status = TournamentRoundStatus.InProgress
        };
        db.TournamentRounds.Add(round);
        await db.SaveChangesAsync();

        int half = ranked.Count / 2;
        var used = new HashSet<int>();
        for (int i = 0; i < half; i++)
        {
            var white = ranked[i];
            var black = ranked[ranked.Count - 1 - i];
            used.Add(white.Id);
            used.Add(black.Id);

            if (HasPlayed(allMatches, white.PlayerId, black.PlayerId))
            {
                var replacement = ranked
                    .Where(p => !used.Contains(p.Id))
                    .FirstOrDefault(p => !HasPlayed(allMatches, p.PlayerId, white.PlayerId));
                if (replacement is not null)
                {
                    black = replacement;
                }
            }

            int whiteCount = allMatches.Count(m => m.WhitePlayerId == white.PlayerId);
            int blackCount = allMatches.Count(m => m.WhitePlayerId == black.PlayerId);
            if (blackCount < whiteCount)
            {
                (white, black) = (black, white);
            }

            var game = new Game
            {
                GameKey = Guid.NewGuid(),
                Mode = GameMode.Tournament,
                Status = GameStatus.Waiting,
                WhitePlayerId = white.PlayerId,
                BlackPlayerId = black.PlayerId,
                BaseMinutes = tournament.BaseMinutes,
                IncrementSeconds = tournament.IncrementSeconds,
                WhiteMsLeft = tournament.BaseMinutes * 60_000L,
                BlackMsLeft = tournament.BaseMinutes * 60_000L,
                CreatedUtc = DateTime.UtcNow
            };
            db.Games.Add(game);
            await db.SaveChangesAsync();

            var match = new TournamentMatch
            {
                RoundId = round.Id,
                TournamentId = tournament.Id,
                WhitePlayerId = white.PlayerId,
                BlackPlayerId = black.PlayerId,
                GameId = game.Id
            };
            db.TournamentMatches.Add(match);
            await db.SaveChangesAsync();

            game.TournamentMatchId = match.Id;
        }

        await db.SaveChangesAsync();
    }

    private static bool HasPlayed(List<TournamentMatch> matches, string playerA, string playerB)
    {
        return matches.Any(m =>
            (m.WhitePlayerId == playerA && m.BlackPlayerId == playerB) ||
            (m.WhitePlayerId == playerB && m.BlackPlayerId == playerA));
    }
}