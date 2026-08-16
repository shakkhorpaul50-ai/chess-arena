using ChessDotNetCore;
using EngineGame = ChessDotNetCore.ChessGame;
using EngineResult = ChessDotNetCore.GameResult;
using GameResult = WebApplication1.Models.GameResult;

namespace WebApplication1.Services;

public static class ChessGameService
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public static EngineGame CreateEngine(string? fen = null)
    {
        return new EngineGame(fen ?? StartFen);
    }

    public static EngineGame RebuildEngine(IEnumerable<Models.GameMove> moves)
    {
        var engine = CreateEngine();
        foreach (var m in moves)
        {
            var player = m.IsWhite ? Player.White : Player.Black;
            var move = new Move(m.From, m.To, player, m.Promotion is null ? null : m.Promotion[0]);
            engine.MakeMove(move, true);
        }
        return engine;
    }

    public static string? ApplyMove(EngineGame engine, string from, string to, char? promotion, Player player)
    {
        var move = new Move(from, to, player, promotion);
        var type = engine.MakeMove(move, false);
        if (type == MoveType.Invalid)
        {
            return null;
        }
        return engine.LastMove?.SAN;
    }

    public static (bool Ended, GameResult Result) GetStatus(EngineGame engine)
    {
        switch (engine.GameResult)
        {
            case EngineResult.Mate:
                return (true, engine.CurrentPlayer == Player.White ? GameResult.BlackWin : GameResult.WhiteWin);
            case EngineResult.Stalemated:
            case EngineResult.ThreeFoldRepeat:
            case EngineResult.FiftyRuleRepeat:
            case EngineResult.InsufficientMaterial:
                return (true, GameResult.Draw);
            default:
                return (false, GameResult.Undecided);
        }
    }

    public static string BuildPgn(string existing, string san, int moveNumber)
    {
        if (moveNumber % 2 == 1)
        {
            int fullMove = (moveNumber + 1) / 2;
            return existing + $"{fullMove}. {san} ";
        }
        return existing + san + " ";
    }
}