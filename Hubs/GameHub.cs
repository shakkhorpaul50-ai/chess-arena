using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WebApplication1.Data;
using WebApplication1.Services;

namespace WebApplication1.Hubs;

[Authorize]
public class GameHub : Hub
{
    private readonly GameSessionManager _manager;
    private readonly BotService _botService;
    private readonly ILogger<GameHub> _logger;

    public GameHub(GameSessionManager manager, BotService botService, ILogger<GameHub> logger)
    {
        _manager = manager;
        _botService = botService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (userId is not null)
        {
            try
            {
                using var scope = Context.GetHttpContext()!.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await db.Users.FindAsync(userId);
                if (user is not null)
                {
                    await _manager.RegisterConnectionAsync(userId, Context.ConnectionId, user.DisplayName, user.Rating);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Presence registration failed for {UserId}", userId);
            }
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (userId is not null)
        {
            await _manager.UnregisterConnectionAsync(userId, Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public Task<List<OnlineUser>> GetOnlineUsers()
    {
        return Task.FromResult(_manager.GetOnlineUsers().ToList());
    }

    public Task<List<LobbyGame>> GetLobbyGames()
    {
        return _manager.GetLobbyGamesAsync();
    }

    public async Task<HubResult> CreateChallenge(string targetUserId, int baseMinutes, int incrementSeconds)
    {
        var me = Context.UserIdentifier!;
        var name = Context.User?.Identity?.Name ?? me;
        return await _manager.ChallengeAsync(me, targetUserId, name, baseMinutes, incrementSeconds);
    }

    public Task<HubResult> CancelChallenge(Guid gameKey)
    {
        return _manager.CancelChallengeAsync(gameKey, Context.UserIdentifier!);
    }

    public Task<HubResult> DeclineChallenge(Guid gameKey)
    {
        return _manager.DeclineChallengeAsync(gameKey, Context.UserIdentifier!);
    }

    public async Task<GameSnapshot?> AcceptChallenge(Guid gameKey)
    {
        var snapshot = await _manager.AcceptChallengeAsync(gameKey, Context.UserIdentifier!, Context.ConnectionId);
        if (snapshot is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(gameKey));
        }
        return snapshot;
    }

    public async Task<GameSnapshot?> JoinGame(Guid gameKey)
    {
        var snapshot = await _manager.JoinGameAsync(gameKey, Context.UserIdentifier!, Context.ConnectionId, spectator: false);
        if (snapshot is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(gameKey));
        }
        return snapshot;
    }

    public async Task<GameSnapshot?> SpectateGame(Guid gameKey)
    {
        var snapshot = await _manager.JoinGameAsync(gameKey, Context.UserIdentifier!, Context.ConnectionId, spectator: true);
        if (snapshot is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(gameKey));
        }
        return snapshot;
    }

    public async Task<MoveOutcome> PlayMove(Guid gameKey, string from, string to, string? promotion)
    {
        char? promo = promotion is { Length: 1 } ? promotion[0] : null;
        var outcome = await _manager.ApplyMoveAsync(gameKey, Context.UserIdentifier!, from, to, promo);
        if (outcome.Ok && !outcome.Ended && outcome.BotMustMove)
        {
            _botService.QueueBotMove(gameKey, _manager.GetBotDifficulty(gameKey));
        }
        return outcome;
    }

    public Task<HubResult> Resign(Guid gameKey)
    {
        return _manager.ResignAsync(gameKey, Context.UserIdentifier!);
    }

    public Task<HubResult> OfferDraw(Guid gameKey)
    {
        return _manager.OfferDrawAsync(gameKey, Context.UserIdentifier!);
    }

    public Task<HubResult> AcceptDraw(Guid gameKey)
    {
        return _manager.AcceptDrawAsync(gameKey, Context.UserIdentifier!);
    }

    public Task<HubResult> DeclineDraw(Guid gameKey)
    {
        return _manager.DeclineDrawAsync(gameKey, Context.UserIdentifier!);
    }

    public async Task<GameSnapshot?> RequestRematch(Guid gameKey)
    {
        var userId = Context.UserIdentifier!;
        var newKey = await _manager.RequestRematchAsync(gameKey, userId);
        if (newKey is null || newKey == gameKey)
        {
            return null;
        }
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(gameKey));
        var snapshot = await _manager.JoinGameAsync(newKey.Value, userId, Context.ConnectionId, spectator: false);
        if (snapshot is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(newKey.Value));
            if (snapshot.BotGame && snapshot.Status == "Active" && !snapshot.MyTurn)
            {
                _botService.QueueBotMove(newKey.Value, _manager.GetBotDifficulty(newKey.Value));
            }
        }
        return snapshot;
    }

    public async Task JoinTournament(int tournamentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, TournamentGroupName(tournamentId));
    }

    public async Task LeaveTournament(int tournamentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TournamentGroupName(tournamentId));
    }

    private static string GroupName(Guid gameKey) => $"game:{gameKey}";

    private static string TournamentGroupName(int tournamentId) => $"tournament:{tournamentId}";
}