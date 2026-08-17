using System.Collections.Concurrent;
using ChessDotNetCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Hubs;
using WebApplication1.Models;
using EngineGame = ChessDotNetCore.ChessGame;
using GameResult = WebApplication1.Models.GameResult;

namespace WebApplication1.Services;

public sealed class GameSessionManager
{
    private sealed class GameSession
    {
        public Guid GameKey { get; init; }
        public int DbGameId { get; init; }
        public string WhiteUserId { get; init; } = "";
        public string WhiteName { get; set; } = "";
        public int WhiteRating { get; set; }
        public string BlackUserId { get; init; } = "";
        public string BlackName { get; set; } = "";
        public int BlackRating { get; set; }
        public bool IsBotWhite { get; init; }
        public bool IsBotBlack { get; init; }
        public GameMode Mode { get; init; }
        public long BaseMs { get; init; }
        public long IncrementMs { get; init; }
        public long WhiteMsLeft { get; set; }
        public long BlackMsLeft { get; set; }
        public DateTime LastMoveUtc { get; set; }
        public DateTime? StartedUtc { get; set; }
        public EngineGame Engine { get; set; } = ChessGameService.CreateEngine();
        public GameStatus Status { get; set; } = GameStatus.Waiting;
        public GameResult Result { get; set; } = GameResult.Undecided;
        public string? WinnerUserId { get; set; }
        public string? DrawOfferByUserId { get; set; }
        public bool WhiteRematchRequested { get; set; }
        public bool BlackRematchRequested { get; set; }
        public int MoveCount { get; set; }
        public string Pgn { get; set; } = "";
        public bool Ended { get; set; }
        public string Difficulty { get; init; } = "medium";
        public readonly object Sync = new();
        public readonly Dictionary<string, string> PlayerConnections = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> SpectatorConnections = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<GameHub> _hub;
    private readonly ILogger<GameSessionManager> _logger;

    private readonly ConcurrentDictionary<string, HashSet<string>> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (string Name, int Rating)> _onlineUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, GameSession> _sessions = new();
    private readonly ConcurrentDictionary<string, Guid> _challenges = new(StringComparer.OrdinalIgnoreCase);

    public GameSessionManager(IServiceScopeFactory scopeFactory, IHubContext<GameHub> hub, ILogger<GameSessionManager> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;
    }

    public IReadOnlyList<OnlineUser> GetOnlineUsers()
    {
        return _onlineUsers.Select(u => new OnlineUser(u.Key, u.Value.Name, u.Value.Rating)).ToList();
    }

    public int SpectatorCount(Guid gameKey)
    {
        return _sessions.TryGetValue(gameKey, out var s) ? s.SpectatorConnections.Count : 0;
    }

    public async Task RegisterConnectionAsync(string userId, string connectionId, string displayName, int rating)
    {
        var conns = _connections.GetOrAdd(userId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        lock (conns)
        {
            conns.Add(connectionId);
        }
        _onlineUsers[userId] = (displayName, rating);
        await BroadcastPresenceAsync();
    }

    public async Task UnregisterConnectionAsync(string userId, string connectionId)
    {
        if (_connections.TryGetValue(userId, out var conns))
        {
            lock (conns)
            {
                conns.Remove(connectionId);
                if (conns.Count == 0)
                {
                    _connections.TryRemove(userId, out _);
                    _onlineUsers.TryRemove(userId, out _);
                }
            }
        }

        var disconnectedGames = new List<Guid>();
        foreach (var kvp in _sessions)
        {
            var s = kvp.Value;
            bool notifyDisconnect = false;
            lock (s.Sync)
            {
                bool removedPlayer = s.PlayerConnections.Remove(connectionId);
                bool removedSpectator = s.SpectatorConnections.Remove(connectionId);

                if (removedPlayer && s.PlayerConnections.Count == 0)
                {
                    if (s.Status == GameStatus.Waiting)
                    {
                        disconnectedGames.Add(s.GameKey);
                    }
                    else if (s.Status == GameStatus.Active)
                    {
                        notifyDisconnect = true;
                    }
                }
                else if (removedSpectator)
                {
                    _ = _hub.Clients.Group(GroupName(s.GameKey)).SendAsync("SpectatorsChanged", new SpectatorsEvent(s.GameKey, s.SpectatorConnections.Count));
                }
            }
            if (notifyDisconnect)
            {
                await _hub.Clients.Group(GroupName(s.GameKey)).SendAsync("PlayerDisconnected", new PlayerDisconnectedEvent(s.GameKey, userId));
            }
        }

        foreach (var key in disconnectedGames)
        {
            if (_sessions.TryGetValue(key, out var s))
            {
                bool abort = false;
                lock (s.Sync)
                {
                    abort = s.Status == GameStatus.Waiting;
                }
                if (abort)
                {
                    await AbortWaitingGameAsync(key);
                }
                else
                {
                    await _hub.Clients.Group(GroupName(key)).SendAsync("PlayerDisconnected", new PlayerDisconnectedEvent(key, userId));
                }
            }
        }

        await BroadcastPresenceAsync();
    }

    private async Task AbortWaitingGameAsync(Guid gameKey)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return;
        }
        bool shouldAbort = false;
        lock (s.Sync)
        {
            shouldAbort = s.Status == GameStatus.Waiting;
            if (shouldAbort)
            {
                s.Status = GameStatus.Aborted;
                s.Result = GameResult.Aborted;
            }
        }
        if (shouldAbort)
        {
            _challenges.TryRemove(s.BlackUserId, out _);
            _sessions.TryRemove(gameKey, out _);
            await PersistAbortAsync(s.DbGameId);
            await _hub.Clients.Group(GroupName(gameKey)).SendAsync("GameOver", new GameOverEvent(gameKey, "aborted", null, "The game was cancelled.", s.Engine.GetFen()));
            await BroadcastLobbyAsync();
        }
    }

    public async Task<HubResult> ChallengeAsync(string fromUserId, string toUserId, string fromName, int baseMinutes, int incrementSeconds)
    {
        if (string.Equals(fromUserId, toUserId, StringComparison.OrdinalIgnoreCase))
        {
            return new HubResult(false, "You cannot challenge yourself.");
        }
        if (_challenges.ContainsKey(toUserId))
        {
            return new HubResult(false, "That player already has a pending challenge.");
        }

        var scope = _scopeFactory.CreateScope();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var toUser = await db.Users.FindAsync(toUserId);
            if (toUser is null)
            {
                return new HubResult(false, "Player not found.");
            }
            var fromUser = await db.Users.FindAsync(fromUserId);
            if (fromUser is null)
            {
                return new HubResult(false, "You must be logged in.");
            }

            var game = new Game
            {
                GameKey = Guid.NewGuid(),
                Mode = GameMode.PvP,
                Status = GameStatus.Waiting,
                WhitePlayerId = fromUserId,
                BlackPlayerId = toUserId,
                BaseMinutes = baseMinutes,
                IncrementSeconds = incrementSeconds,
                WhiteMsLeft = baseMinutes * 60_000L,
                BlackMsLeft = baseMinutes * 60_000L,
                CreatedUtc = DateTime.UtcNow
            };
            db.Games.Add(game);
            await db.SaveChangesAsync();

            var session = new GameSession
            {
                GameKey = game.GameKey,
                DbGameId = game.Id,
                WhiteUserId = fromUserId,
                WhiteName = fromUser.DisplayName,
                WhiteRating = fromUser.Rating,
                BlackUserId = toUserId,
                BlackName = toUser.DisplayName,
                BlackRating = toUser.Rating,
                Mode = GameMode.PvP,
                BaseMs = baseMinutes * 60_000L,
                IncrementMs = incrementSeconds * 1000L,
                WhiteMsLeft = baseMinutes * 60_000L,
                BlackMsLeft = baseMinutes * 60_000L,
                LastMoveUtc = DateTime.UtcNow
            };

            _sessions[game.GameKey] = session;
            _challenges[toUserId] = game.GameKey;

            await _hub.Clients.User(toUserId).SendAsync("ChallengeReceived", new ChallengeReceivedEvent(game.GameKey, fromUserId, fromName, baseMinutes, incrementSeconds));
            await BroadcastLobbyAsync();
            return new HubResult(true);
        }
        finally
        {
            scope.Dispose();
        }
    }

    public async Task<HubResult> CancelChallengeAsync(Guid gameKey, string userId)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return new HubResult(false, "Game not found.");
        }
        bool canCancel = false;
        lock (s.Sync)
        {
            canCancel = s.Status == GameStatus.Waiting && s.WhiteUserId == userId && s.Mode == GameMode.PvP;
        }
        if (!canCancel)
        {
            return new HubResult(false, "You cannot cancel this game.");
        }
        await AbortWaitingGameAsync(gameKey);
        return new HubResult(true);
    }

    public async Task<HubResult> DeclineChallengeAsync(Guid gameKey, string userId)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return new HubResult(false, "Game not found.");
        }
        bool canDecline = false;
        lock (s.Sync)
        {
            canDecline = s.Status == GameStatus.Waiting && s.BlackUserId == userId && s.Mode == GameMode.PvP;
        }
        if (!canDecline)
        {
            return new HubResult(false, "You cannot decline this game.");
        }
        await AbortWaitingGameAsync(gameKey);
        return new HubResult(true);
    }

    public async Task<GameSnapshot?> AcceptChallengeAsync(Guid gameKey, string userId, string connectionId)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return null;
        }
        bool accepted = false;
        lock (s.Sync)
        {
            accepted = s.Status == GameStatus.Waiting && s.BlackUserId == userId && s.Mode == GameMode.PvP;
            if (accepted)
            {
                s.PlayerConnections[connectionId] = userId;
            }
        }
        if (!accepted)
        {
            return null;
        }
        _challenges.TryRemove(userId, out _);
        await _hub.Clients.User(s.WhiteUserId).SendAsync("ChallengeAccepted", new ChallengeAcceptedEvent(gameKey));
        await StartIfReadyAsync(s);
        return BuildSnapshot(s, userId, connectionId, isPlayer: true, isSpectator: false);
    }

    public async Task<GameSnapshot?> JoinGameAsync(Guid gameKey, string userId, string connectionId, bool spectator)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            s = await RebuildSessionAsync(gameKey);
            if (s is null)
            {
                return null;
            }
            _sessions[gameKey] = s;
        }

        bool isPlayer = !spectator && (s.WhiteUserId == userId || s.BlackUserId == userId);
        lock (s.Sync)
        {
            if (spectator)
            {
                s.SpectatorConnections.Add(connectionId);
            }
            else if (isPlayer)
            {
                s.PlayerConnections[connectionId] = userId;
            }
            else if (s.BlackUserId == userId || s.WhiteUserId == userId)
            {
                isPlayer = true;
                s.PlayerConnections[connectionId] = userId;
            }
            else
            {
                s.SpectatorConnections.Add(connectionId);
            }
        }

        await StartIfReadyAsync(s);

        if (spectator)
        {
            await _hub.Clients.Group(GroupName(gameKey)).SendAsync("SpectatorsChanged", new SpectatorsEvent(gameKey, s.SpectatorConnections.Count));
        }

        return BuildSnapshot(s, userId, connectionId, isPlayer, spectator);
    }

    private async Task StartIfReadyAsync(GameSession s)
    {
        bool start = false;
        lock (s.Sync)
        {
            if (s.Status != GameStatus.Waiting)
            {
                return;
            }
            if (s.Mode == GameMode.Bot)
            {
                start = s.PlayerConnections.Count > 0;
            }
            else
            {
                bool whiteReady = s.IsBotWhite || s.PlayerConnections.Values.Contains(s.WhiteUserId);
                bool blackReady = s.IsBotBlack || s.PlayerConnections.Values.Contains(s.BlackUserId);
                start = whiteReady && blackReady;
            }
            if (start)
            {
                s.Status = GameStatus.Active;
                s.StartedUtc = DateTime.UtcNow;
                s.LastMoveUtc = s.StartedUtc.Value;
            }
        }
        if (start)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Games.FindAsync(s.DbGameId);
            if (row is not null)
            {
                row.Status = GameStatus.Active;
                row.StartedUtc = s.StartedUtc;
                row.LastMoveUtc = s.LastMoveUtc;
                await db.SaveChangesAsync();
            }
            await _hub.Clients.Group(GroupName(s.GameKey)).SendAsync("GameStarted", new GameStartedEvent(s.GameKey));
            await BroadcastLobbyAsync();
        }
    }

    public async Task<MoveOutcome> ApplyMoveAsync(Guid gameKey, string userId, string from, string to, char? promotion)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return new MoveOutcome(false, "Game not found.", false, false, null, null);
        }

        bool isWhite;
        string san;
        string fen;
        bool ended;
        GameResult result = GameResult.Undecided;
        string? winnerId = null;
        string reason = "";
        bool isCheck = false;

        lock (s.Sync)
        {
            if (s.Status != GameStatus.Active)
            {
                return new MoveOutcome(false, "The game is not active.", false, false, null, null);
            }
            if (s.Ended)
            {
                return new MoveOutcome(false, "The game has ended.", false, false, null, null);
            }
            isWhite = s.WhiteUserId == userId;
            if (!isWhite && s.BlackUserId != userId)
            {
                return new MoveOutcome(false, "You are not a player in this game.", false, false, null, null);
            }
            if ((isWhite && s.IsBotWhite) || (!isWhite && s.IsBotBlack))
            {
                return new MoveOutcome(false, "Invalid player.", false, false, null, null);
            }
            var color = isWhite ? Player.White : Player.Black;
            if (s.Engine.CurrentPlayer != color)
            {
                return new MoveOutcome(false, "It is not your turn.", false, false, null, null);
            }
            if (GetRemainingForSideToMove(s) <= 0)
            {
                return new MoveOutcome(false, "Your time has run out.", false, false, null, null);
            }

            san = ChessGameService.ApplyMove(s.Engine, from, to, promotion, color) ?? "";
            if (string.IsNullOrEmpty(san))
            {
                return new MoveOutcome(false, "Illegal move.", false, false, null, null);
            }

            var now = DateTime.UtcNow;
            var elapsed = now - s.LastMoveUtc;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }
            if (isWhite)
            {
                s.WhiteMsLeft = Math.Max(0, s.WhiteMsLeft - (long)elapsed.TotalMilliseconds) + s.IncrementMs;
            }
            else
            {
                s.BlackMsLeft = Math.Max(0, s.BlackMsLeft - (long)elapsed.TotalMilliseconds) + s.IncrementMs;
            }
            s.LastMoveUtc = now;
            s.MoveCount++;
            s.Pgn = ChessGameService.BuildPgn(s.Pgn, san, s.MoveCount);

            var status = ChessGameService.GetStatus(s.Engine);
            ended = status.Ended;
            result = status.Result;
            if (ended)
            {
                s.Ended = true;
                s.Status = GameStatus.Ended;
                s.Result = result;
                s.WinnerUserId = result switch
                {
                    GameResult.WhiteWin => s.WhiteUserId,
                    GameResult.BlackWin => s.BlackUserId,
                    _ => null
                };
                winnerId = s.WinnerUserId;
                reason = result == GameResult.Draw ? "draw" : "checkmate";
                fen = s.Engine.GetFen();
            }
            else
            {
                isCheck = s.Engine.IsInCheck(color == Player.White ? Player.Black : Player.White);
                fen = s.Engine.GetFen();
            }
        }

        await PersistMoveAsync(s, san, from, to, promotion, fen, isWhite);

        if (ended)
        {
            await EndGameAsync(s, result, winnerId, reason, null);
            return new MoveOutcome(true, null, true, false, null,
                new GameOverEvent(gameKey, ResultLabel(result), winnerId, reason, fen));
        }

        var ev = new MovePlayedEvent(gameKey, fen, san, from, to, promotion?.ToString(),
            s.WhiteMsLeft, s.BlackMsLeft, s.MoveCount, isWhite, isCheck, IsMate: false);
        await _hub.Clients.Group(GroupName(gameKey)).SendAsync("MovePlayed", ev);
        return new MoveOutcome(true, null, false, s.IsBotWhite || s.IsBotBlack, ev, null);
    }

    public async Task<MoveOutcome> ApplyBotMoveAsync(Guid gameKey, string from, string to, char? promotion)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return new MoveOutcome(false, "Game not found.", false, false, null, null);
        }

        bool botIsWhite;
        string san;
        string fen;
        bool ended;
        GameResult result = GameResult.Undecided;
        string? winnerId = null;
        string reason = "";

        lock (s.Sync)
        {
            if (s.Status != GameStatus.Active || s.Ended)
            {
                return new MoveOutcome(false, "Game not active.", false, false, null, null);
            }
            botIsWhite = s.IsBotWhite;
            var color = botIsWhite ? Player.White : Player.Black;
            if (s.Engine.CurrentPlayer != color)
            {
                return new MoveOutcome(false, "Not bot's turn.", false, false, null, null);
            }

            san = ChessGameService.ApplyMove(s.Engine, from, to, promotion, color) ?? "";
            if (string.IsNullOrEmpty(san))
            {
                return new MoveOutcome(false, "Illegal move.", false, false, null, null);
            }

            var now2 = DateTime.UtcNow;
            var elapsed2 = now2 - s.LastMoveUtc;
            if (elapsed2 < TimeSpan.Zero)
            {
                elapsed2 = TimeSpan.Zero;
            }
            if (botIsWhite)
            {
                s.WhiteMsLeft = Math.Max(0, s.WhiteMsLeft - (long)elapsed2.TotalMilliseconds) + s.IncrementMs;
            }
            else
            {
                s.BlackMsLeft = Math.Max(0, s.BlackMsLeft - (long)elapsed2.TotalMilliseconds) + s.IncrementMs;
            }
            s.LastMoveUtc = now2;
            s.MoveCount++;
            s.Pgn = ChessGameService.BuildPgn(s.Pgn, san, s.MoveCount);

            var status = ChessGameService.GetStatus(s.Engine);
            ended = status.Ended;
            result = status.Result;
            if (ended)
            {
                s.Ended = true;
                s.Status = GameStatus.Ended;
                s.Result = result;
                s.WinnerUserId = result switch
                {
                    GameResult.WhiteWin => s.WhiteUserId,
                    GameResult.BlackWin => s.BlackUserId,
                    _ => null
                };
                winnerId = s.WinnerUserId;
                reason = result == GameResult.Draw ? "draw" : "checkmate";
                fen = s.Engine.GetFen();
            }
            else
            {
                fen = s.Engine.GetFen();
            }
        }

        await PersistMoveAsync(s, san, from, to, promotion, fen, botIsWhite);

        if (ended)
        {
            await EndGameAsync(s, result, winnerId, reason, null);
            return new MoveOutcome(true, null, true, false, null,
                new GameOverEvent(gameKey, ResultLabel(result), winnerId, reason, fen));
        }

        var ev = new MovePlayedEvent(gameKey, fen, san, from, to, promotion?.ToString(),
            s.WhiteMsLeft, s.BlackMsLeft, s.MoveCount, botIsWhite, false, false);
        await _hub.Clients.Group(GroupName(gameKey)).SendAsync("MovePlayed", ev);
        return new MoveOutcome(true, null, false, false, ev, null);
    }

    public string? GetFenForBot(Guid gameKey)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return null;
        }
        lock (s.Sync)
        {
            if (s.Status != GameStatus.Active || s.Ended)
            {
                return null;
            }
            return s.Engine.GetFen();
        }
    }

    public string GetBotDifficulty(Guid gameKey)
    {
        return _sessions.TryGetValue(gameKey, out var s) ? s.Difficulty : "medium";
    }

    public string? GetRandomLegalMove(Guid gameKey)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return null;
        }
        lock (s.Sync)
        {
            if (s.Status != GameStatus.Active || s.Ended)
            {
                return null;
            }
            var moves = s.Engine.GetValidMoves(s.Engine.CurrentPlayer);
            if (moves.Count == 0)
            {
                return null;
            }
            var m = moves[Random.Shared.Next(moves.Count)];
            var promo = m.Promotion ?? ' ';
            return m.OriginalPosition.ToString() + m.NewPosition.ToString() + (promo == ' ' ? "" : promo.ToString());
        }
    }

    public async Task<HubResult> ResignAsync(Guid gameKey, string userId)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return new HubResult(false, "Game not found.");
        }
        bool isWhite;
        lock (s.Sync)
        {
            if (s.Status != GameStatus.Active || s.Ended)
            {
                return new HubResult(false, "Game not active.");
            }
            isWhite = s.WhiteUserId == userId;
            if (!isWhite && s.BlackUserId != userId)
            {
                return new HubResult(false, "You are not a player.");
            }
            s.Ended = true;
            s.Status = GameStatus.Ended;
            s.Result = isWhite ? GameResult.BlackWin : GameResult.WhiteWin;
            s.WinnerUserId = isWhite ? s.BlackUserId : s.WhiteUserId;
        }
        await EndGameAsync(s, s.Result, s.WinnerUserId, "resignation", null);
        return new HubResult(true);
    }

    public async Task<HubResult> OfferDrawAsync(Guid gameKey, string userId)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return new HubResult(false, "Game not found.");
        }
        lock (s.Sync)
        {
            if (s.Status != GameStatus.Active || s.Ended)
            {
                return new HubResult(false, "Game not active.");
            }
            if (s.DrawOfferByUserId is not null)
            {
                return new HubResult(false, "A draw offer is already pending.");
            }
            s.DrawOfferByUserId = userId;
        }
        await _hub.Clients.Group(GroupName(gameKey)).SendAsync("DrawOffered", new DrawOfferEvent(gameKey, userId));
        return new HubResult(true);
    }

    public async Task<HubResult> AcceptDrawAsync(Guid gameKey, string userId)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return new HubResult(false, "Game not found.");
        }
        bool accept = false;
        lock (s.Sync)
        {
            if (s.Status != GameStatus.Active || s.Ended)
            {
                return new HubResult(false, "Game not active.");
            }
            accept = s.DrawOfferByUserId is not null && s.DrawOfferByUserId != userId;
            if (accept)
            {
                s.Ended = true;
                s.Status = GameStatus.Ended;
                s.Result = GameResult.Draw;
                s.WinnerUserId = null;
            }
        }
        if (accept)
        {
            await EndGameAsync(s, GameResult.Draw, null, "draw agreement", null);
            return new HubResult(true);
        }
        return new HubResult(false, "No draw offer to accept.");
    }

    public async Task<HubResult> DeclineDrawAsync(Guid gameKey, string userId)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return new HubResult(false, "Game not found.");
        }
        lock (s.Sync)
        {
            if (s.DrawOfferByUserId is not null)
            {
                s.DrawOfferByUserId = null;
            }
        }
        await _hub.Clients.Group(GroupName(gameKey)).SendAsync("DrawOffered", new DrawOfferEvent(gameKey, null));
        return new HubResult(true);
    }

    public async Task<Guid?> RequestRematchAsync(Guid gameKey, string userId)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            return null;
        }
        bool ready = false;
        lock (s.Sync)
        {
            if (!s.Ended)
            {
                return null;
            }
            if (s.WhiteUserId == userId)
            {
                s.WhiteRematchRequested = true;
            }
            else if (s.BlackUserId == userId)
            {
                s.BlackRematchRequested = true;
            }
            else
            {
                return null;
            }
            if (s.Mode == GameMode.Bot)
            {
                ready = true;
            }
            else
            {
                ready = s.WhiteRematchRequested && s.BlackRematchRequested;
            }
        }
        if (!ready)
        {
            await _hub.Clients.Group(GroupName(gameKey)).SendAsync("RematchRequested", new RematchEvent(gameKey, null));
            return null;
        }
        return await CreateRematchAsync(s);
    }

    private async Task<Guid> CreateRematchAsync(GameSession old)
    {
        var newKey = Guid.NewGuid();
        var scope = _scopeFactory.CreateScope();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var whiteUser = await db.Users.FindAsync(old.BlackUserId);
            var blackUser = await db.Users.FindAsync(old.WhiteUserId);
            if (whiteUser is null || blackUser is null)
            {
                return old.GameKey;
            }
            int baseMin = (int)(old.BaseMs / 60_000);
            int inc = (int)(old.IncrementMs / 1000);
            var game = new Game
            {
                GameKey = newKey,
                Mode = old.Mode,
                Status = old.Mode == GameMode.Bot ? GameStatus.Active : GameStatus.Waiting,
                WhitePlayerId = old.BlackUserId,
                BlackPlayerId = old.WhiteUserId,
                BaseMinutes = baseMin,
                IncrementSeconds = inc,
                WhiteMsLeft = old.BaseMs,
                BlackMsLeft = old.BaseMs,
                CreatedUtc = DateTime.UtcNow,
                Difficulty = old.Difficulty
            };
            if (old.Mode == GameMode.Bot)
            {
                game.StartedUtc = DateTime.UtcNow;
                game.LastMoveUtc = game.StartedUtc;
            }
            db.Games.Add(game);
            await db.SaveChangesAsync();

            var session = new GameSession
            {
                GameKey = newKey,
                DbGameId = game.Id,
                WhiteUserId = old.BlackUserId,
                WhiteName = old.BlackName,
                WhiteRating = old.BlackRating,
                BlackUserId = old.WhiteUserId,
                BlackName = old.WhiteName,
                BlackRating = old.WhiteRating,
                IsBotWhite = old.IsBotBlack,
                IsBotBlack = old.IsBotWhite,
                Mode = old.Mode,
                BaseMs = old.BaseMs,
                IncrementMs = old.IncrementMs,
                WhiteMsLeft = old.BaseMs,
                BlackMsLeft = old.BaseMs,
                LastMoveUtc = DateTime.UtcNow,
                Status = old.Mode == GameMode.Bot ? GameStatus.Active : GameStatus.Waiting,
                Difficulty = old.Difficulty
            };
            if (old.Mode == GameMode.Bot)
            {
                session.StartedUtc = DateTime.UtcNow;
            }
            _sessions[newKey] = session;

            await _hub.Clients.Group(GroupName(old.GameKey)).SendAsync("RematchStarted", new RematchEvent(old.GameKey, newKey.ToString()));
            await BroadcastLobbyAsync();
            return newKey;
        }
        finally
        {
            scope.Dispose();
        }
    }

    public async Task CheckClocksAsync()
    {
        foreach (var kvp in _sessions)
        {
            var s = kvp.Value;
            GameResult result = GameResult.Undecided;
            string? winnerId = null;
            bool forfeit = false;

            lock (s.Sync)
            {
                if (s.Status != GameStatus.Active || s.Ended)
                {
                    continue;
                }
                long remaining = GetRemainingForSideToMove(s);
                var now = DateTime.UtcNow;
                long whiteMs = s.WhiteMsLeft;
                long blackMs = s.BlackMsLeft;
                bool whiteToMove = s.Engine.CurrentPlayer == Player.White;
                if (whiteToMove)
                {
                    whiteMs = Math.Max(0, s.WhiteMsLeft - (long)(now - s.LastMoveUtc).TotalMilliseconds);
                }
                else
                {
                    blackMs = Math.Max(0, s.BlackMsLeft - (long)(now - s.LastMoveUtc).TotalMilliseconds);
                }

                if (remaining <= 0)
                {
                    forfeit = true;
                    s.Ended = true;
                    s.Status = GameStatus.Ended;
                    s.Result = whiteToMove ? GameResult.BlackWin : GameResult.WhiteWin;
                    s.WinnerUserId = whiteToMove ? s.BlackUserId : s.WhiteUserId;
                    result = s.Result;
                    winnerId = s.WinnerUserId;
                }
                else
                {
                    _ = _hub.Clients.Group(GroupName(s.GameKey)).SendAsync("ClockTick",
                        new ClockTickEvent(s.GameKey, whiteMs, blackMs, whiteToMove ? "white" : "black"));
                }
            }

            if (forfeit)
            {
                await EndGameAsync(s, result, winnerId, "timeout", null);
            }
        }
    }

    public async Task<GameSnapshot?> GetSnapshotForPageAsync(Guid gameKey, string userId)
    {
        if (!_sessions.TryGetValue(gameKey, out var s))
        {
            s = await RebuildSessionAsync(gameKey);
            if (s is null)
            {
                return null;
            }
            _sessions[gameKey] = s;
        }
        bool isPlayer = s.WhiteUserId == userId || s.BlackUserId == userId;
        return BuildSnapshot(s, userId, null, isPlayer, !isPlayer);
    }

    public Task<List<LobbyGame>> GetLobbyGamesAsync()
    {
        var list = new List<LobbyGame>();
        foreach (var kvp in _sessions)
        {
            var s = kvp.Value;
            if (s.Mode != GameMode.PvP)
            {
                continue;
            }
            list.Add(new LobbyGame(
                s.GameKey,
                s.WhiteName,
                s.BlackName,
                s.Mode.ToString(),
                $"{s.BaseMs / 60_000}+{s.IncrementMs / 1000}",
                s.Status.ToString(),
                false));
        }
        return Task.FromResult(list);
    }

    public async Task BroadcastLobbyAsync()
    {
        var games = await GetLobbyGamesAsync();
        await _hub.Clients.All.SendAsync("LobbyRefresh", new LobbyEvent(games));
    }

    public async Task BroadcastPresenceAsync()
    {
        var users = GetOnlineUsers().ToList();
        await _hub.Clients.All.SendAsync("Presence", new PresenceEvent(users));
    }

    private static string GroupName(Guid gameKey) => $"game:{gameKey}";

    private static long GetRemainingForSideToMove(GameSession s)
    {
        bool whiteToMove = s.Engine.CurrentPlayer == Player.White;
        var elapsed = DateTime.UtcNow - s.LastMoveUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        long remaining = whiteToMove ? s.WhiteMsLeft : s.BlackMsLeft;
        remaining -= (long)elapsed.TotalMilliseconds;
        return remaining;
    }

    private static string ResultLabel(GameResult r) => r switch
    {
        GameResult.WhiteWin => "white_win",
        GameResult.BlackWin => "black_win",
        GameResult.Draw => "draw",
        GameResult.Aborted => "aborted",
        _ => "undecided"
    };

    private async Task PersistMoveAsync(GameSession s, string san, string from, string to, char? promotion, string fen, bool isWhite)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Games.FindAsync(s.DbGameId);
            if (row is null)
            {
                return;
            }
            row.Fen = fen;
            row.Pgn = s.Pgn;
            row.WhiteMsLeft = s.WhiteMsLeft;
            row.BlackMsLeft = s.BlackMsLeft;
            row.LastMoveUtc = s.LastMoveUtc;

            db.GameMoves.Add(new GameMove
            {
                GameId = s.DbGameId,
                MoveNumber = s.MoveCount,
                San = san,
                From = from,
                To = to,
                Promotion = promotion?.ToString(),
                FenAfter = fen,
                MsLeftAfter = isWhite ? s.WhiteMsLeft : s.BlackMsLeft,
                IsWhite = isWhite,
                PlayedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist move for game {GameKey}", s.GameKey);
        }
    }

    private async Task PersistAbortAsync(int dbGameId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Games.FindAsync(dbGameId);
            if (row is null)
            {
                return;
            }
            row.Status = GameStatus.Aborted;
            row.Result = GameResult.Aborted;
            row.EndedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist abort for game {GameId}", dbGameId);
        }
    }

    private async Task EndGameAsync(GameSession s, GameResult result, string? winnerId, string reason, string? fenOverride)
    {
        string fen;
        lock (s.Sync)
        {
            fen = fenOverride ?? s.Engine.GetFen();
            s.WhiteMsLeft = Math.Max(0, s.WhiteMsLeft);
            s.BlackMsLeft = Math.Max(0, s.BlackMsLeft);
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Games.FindAsync(s.DbGameId);
            if (row is not null)
            {
                row.Status = GameStatus.Ended;
                row.Result = result;
                row.WinnerUserId = winnerId;
                row.EndedUtc = DateTime.UtcNow;
                row.Fen = fen;
                row.Pgn = s.Pgn;
                row.WhiteMsLeft = s.WhiteMsLeft;
                row.BlackMsLeft = s.BlackMsLeft;
                row.LastMoveUtc = s.LastMoveUtc;
                await db.SaveChangesAsync();

                if (result != GameResult.Aborted && result != GameResult.Undecided)
                {
                    var stats = scope.ServiceProvider.GetRequiredService<StatsService>();
                    await stats.ApplyGameResultAsync(db, s.DbGameId);

                    if (s.Mode == GameMode.Tournament && row.TournamentMatchId.HasValue)
                    {
                        var tournaments = scope.ServiceProvider.GetRequiredService<TournamentService>();
                        await tournaments.CompleteMatchAsync(db, row.TournamentMatchId.Value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finalize game {GameKey}", s.GameKey);
        }

        await _hub.Clients.Group(GroupName(s.GameKey)).SendAsync("GameOver",
            new GameOverEvent(s.GameKey, ResultLabel(result), winnerId, reason, fen));
        await BroadcastLobbyAsync();
    }

    private async Task<GameSession?> RebuildSessionAsync(Guid gameKey)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Games
                .Include(g => g.WhitePlayer)
                .Include(g => g.BlackPlayer)
                .Include(g => g.Moves)
                .FirstOrDefaultAsync(g => g.GameKey == gameKey);
            if (row is null)
            {
                return null;
            }

            var moves = row.Moves.OrderBy(m => m.MoveNumber).ToList();
            var engine = moves.Count == 0 ? ChessGameService.CreateEngine() : ChessGameService.RebuildEngine(moves);

            var session = new GameSession
            {
                GameKey = row.GameKey,
                DbGameId = row.Id,
                WhiteUserId = row.WhitePlayerId ?? "",
                WhiteName = row.WhitePlayer?.DisplayName ?? "White",
                WhiteRating = row.WhitePlayer?.Rating ?? 1200,
                BlackUserId = row.BlackPlayerId ?? "",
                BlackName = row.BlackPlayer?.DisplayName ?? "Black",
                BlackRating = row.BlackPlayer?.Rating ?? 1200,
                IsBotWhite = row.WhitePlayer?.IsBot == true,
                IsBotBlack = row.BlackPlayer?.IsBot == true,
                Mode = row.Mode,
                BaseMs = row.BaseMinutes * 60_000L,
                IncrementMs = row.IncrementSeconds * 1000L,
                WhiteMsLeft = row.WhiteMsLeft,
                BlackMsLeft = row.BlackMsLeft,
                LastMoveUtc = row.LastMoveUtc ?? row.CreatedUtc,
                StartedUtc = row.StartedUtc,
                Engine = engine,
                Status = row.Status,
                Result = row.Result,
                WinnerUserId = row.WinnerUserId,
                MoveCount = moves.Count,
                Pgn = row.Pgn,
                Ended = row.Status == GameStatus.Ended,
                Difficulty = row.Difficulty ?? "medium"
            };
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild session {GameKey}", gameKey);
            return null;
        }
    }

    private static GameSnapshot BuildSnapshot(GameSession s, string userId, string? connectionId, bool isPlayer, bool isSpectator)
    {
        bool isWhite = s.WhiteUserId == userId;
        var moves = new List<string>();
        lock (s.Sync)
        {
            foreach (var dm in s.Engine.AllMoves)
            {
                moves.Add(dm.SAN);
            }
        }
        return new GameSnapshot(
            s.GameKey,
            s.DbGameId,
            s.Mode.ToString(),
            s.Status.ToString(),
            ResultLabel(s.Result),
            s.WinnerUserId,
            s.WhiteUserId,
            s.WhiteName,
            s.WhiteRating,
            s.BlackUserId,
            s.BlackName,
            s.BlackRating,
            (int)(s.BaseMs / 60_000),
            (int)(s.IncrementMs / 1000),
            s.WhiteMsLeft,
            s.BlackMsLeft,
            s.Engine.GetFen(),
            s.Pgn,
            moves,
            isPlayer,
            isSpectator,
            isWhite,
            s.Status == GameStatus.Active && !s.Ended && isWhite == (s.Engine.CurrentPlayer == Player.White) && isPlayer,
            s.IsBotWhite || s.IsBotBlack,
            s.DrawOfferByUserId,
            s.Status == GameStatus.Ended ? ResultLabel(s.Result) : "");
    }
}