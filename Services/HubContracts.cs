namespace WebApplication1.Services;

public record HubResult(bool Ok, string? Error = null);

public record OnlineUser(string UserId, string Name, int Rating);

public record PresenceEvent(List<OnlineUser> Users);

public record LobbyGame(
    Guid GameKey,
    string WhiteName,
    string BlackName,
    string Mode,
    string TimeControl,
    string Status,
    bool IsBotGame);

public record LobbyEvent(List<LobbyGame> Games);

public record ChallengeReceivedEvent(Guid GameKey, string FromUserId, string FromName, int BaseMinutes, int IncrementSeconds);

public record GameStartedEvent(Guid GameKey);

public record MovePlayedEvent(
    Guid GameKey,
    string Fen,
    string San,
    string From,
    string To,
    string? Promotion,
    long WhiteMs,
    long BlackMs,
    int MoveNumber,
    bool IsWhite,
    bool IsCheck,
    bool IsMate);

public record ClockTickEvent(Guid GameKey, long WhiteMs, long BlackMs, string Turn);

public record GameOverEvent(Guid GameKey, string Result, string? WinnerUserId, string Reason, string Fen);

public record DrawOfferEvent(Guid GameKey, string? DrawOfferByUserId);

public record RematchEvent(Guid GameKey, string? NewGameKey);

public record PlayerDisconnectedEvent(Guid GameKey, string UserId);

public record SpectatorsEvent(Guid GameKey, int Count);

public record GameSnapshot(
    Guid GameKey,
    int DbGameId,
    string Mode,
    string Status,
    string Result,
    string? WinnerUserId,
    string WhiteUserId,
    string WhiteName,
    int WhiteRating,
    string BlackUserId,
    string BlackName,
    int BlackRating,
    int BaseMinutes,
    int IncrementSeconds,
    long WhiteMs,
    long BlackMs,
    string Fen,
    string Pgn,
    List<string> MoveHistory,
    bool IsPlayer,
    bool IsSpectator,
    bool IsWhite,
    bool MyTurn,
    bool BotGame,
    string? DrawOfferByUserId,
    string Reason);

public record MoveOutcome(
    bool Ok,
    string? Error,
    bool Ended,
    bool BotMustMove,
    MovePlayedEvent? Event,
    GameOverEvent? OverEvent);