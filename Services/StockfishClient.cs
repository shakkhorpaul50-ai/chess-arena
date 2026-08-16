using System.Diagnostics;
using System.Threading.Channels;

namespace WebApplication1.Services;

public sealed class StockfishClient : IDisposable
{
    private readonly string _binaryPath;
    private readonly ILogger<StockfishClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _processLock = new();

    private Process? _process;
    private Channel<string> _lines = Channel.CreateUnbounded<string>();
    private Task? _readerTask;
    private int _currentSkill = -1;

    public StockfishClient(IConfiguration config, ILogger<StockfishClient> logger)
    {
        _binaryPath = config["Stockfish:BinaryPath"] ?? "Stockfish/stockfish";
        _logger = logger;
    }

    public bool IsAvailable => File.Exists(_binaryPath);

    public string? GetBestMove(string fen, int skillLevel, int moveTimeMs)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("Stockfish binary not found at {Path}; falling back to random moves.", _binaryPath);
            return null;
        }

        _gate.Wait();
        try
        {
            EnsureRunning(skillLevel);

            WriteLine($"position fen {fen}");
            WriteLine($"go movetime {moveTimeMs}");

            var deadline = DateTime.UtcNow.AddMilliseconds(moveTimeMs + 8000);
            while (DateTime.UtcNow < deadline)
            {
                var line = ReadLine(TimeSpan.FromMilliseconds(300));
                if (line is null)
                {
                    continue;
                }
                if (line.StartsWith("bestmove ", StringComparison.Ordinal))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && parts[1] != "(none)")
                    {
                        return parts[1];
                    }
                    return null;
                }
            }

            _logger.LogWarning("Stockfish timed out; restarting engine.");
            Kill();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stockfish error; restarting engine.");
            Kill();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureRunning(int skillLevel)
    {
        lock (_processLock)
        {
            if (_process is not null && !_process.HasExited)
            {
                if (_currentSkill != skillLevel)
                {
                    WriteLine($"setoption name Skill Level value {skillLevel}");
                    _currentSkill = skillLevel;
                }
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = _binaryPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            _process = Process.Start(psi);
            if (_process is null)
            {
                throw new InvalidOperationException("Failed to start Stockfish process.");
            }
            _process.StandardInput.NewLine = "\n";

            _lines = Channel.CreateUnbounded<string>();
            _readerTask = Task.Run(ReadLoopAsync);

            WriteLine("uci");
            if (!WaitForLine("uciok", TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException("Stockfish did not respond to uci.");
            }
            WriteLine("setoption name Threads value 1");
            WriteLine("setoption name Hash value 32");
            WriteLine($"setoption name Skill Level value {skillLevel}");
            _currentSkill = skillLevel;
            WriteLine("isready");
            if (!WaitForLine("readyok", TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException("Stockfish did not become ready.");
            }
            _logger.LogInformation("Stockfish engine ready at {Path}", _binaryPath);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            var process = _process;
            if (process is null)
            {
                return;
            }
            while (!process.HasExited && await process.StandardOutput.ReadLineAsync() is { } line)
            {
                await _lines.Writer.WriteAsync(line);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _lines.Writer.TryComplete();
        }
    }

    private bool WaitForLine(string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var line = ReadLine(TimeSpan.FromMilliseconds(300));
            if (line is null)
            {
                continue;
            }
            if (line.StartsWith(expected, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private string? ReadLine(TimeSpan timeout)
    {
        if (_lines.Reader.TryRead(out var line))
        {
            return line;
        }
        var wait = _lines.Reader.WaitToReadAsync().AsTask();
        if (wait.Wait(timeout) && wait.Result)
        {
            _lines.Reader.TryRead(out line);
            return line;
        }
        return null;
    }

    private void WriteLine(string command)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            throw new InvalidOperationException("Stockfish process is not running.");
        }
        process.StandardInput.WriteLine(command);
        process.StandardInput.Flush();
    }

    private void Kill()
    {
        lock (_processLock)
        {
            if (_process is not null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception)
                {
                }
                _process.Dispose();
                _process = null;
            }
            _currentSkill = -1;
        }
    }

    public void Dispose()
    {
        Kill();
        _gate.Dispose();
    }
}