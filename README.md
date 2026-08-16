# ChessArena

Real-time chess platform: PvP with chess clocks, Stockfish bot games, Swiss tournaments, friends, and live spectating.

## Stack

- ASP.NET Core MVC (.NET 10) + Razor views + Tailwind (CDN)
- SignalR for real-time play (`/hubs/game`)
- PostgreSQL via EF Core (Npgsql) — works with Neon and local Docker
- chess.js 0.13.4 (client legality checks), chessboard.js 1.0.0 (board UI)
- Stockfish (GPL-3.0) for the bot — official binaries bundled in Docker, or `scripts/download-stockfish.ps1` for Windows

## Run locally

Prerequisites: .NET 10 SDK, Docker (for Postgres).

```sh
docker run -d --name chess-postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=chessdb -p 5432:5432 postgres:16-alpine
.\scripts\download-stockfish.ps1        # Windows: installs Stockfish/stockfish.exe
dotnet run
```

The app applies EF migrations and seeds data on startup:
- Admin user: `admin@chessarena.app` / `Admin123!` (role `Admin`, change via `Admin__Password` env var)
- Bot user: `ChessBot` (IsBot, rating 1500)

Open http://localhost:5281.

## Configuration (env vars / appsettings)

| Key | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings__Default` | local Postgres | e.g. Neon: `Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require` |
| `Admin__Email` | `admin@chessarena.app` | |
| `Admin__Password` | `Admin123!` | set a strong value in production |
| `Stockfish__BinaryPath` | `Stockfish/stockfish` | `Stockfish/stockfish.exe` on Windows (set in `appsettings.Development.json`) |

## Deploy

- Render: push this repo, create a Blueprint from `render.yaml`, then set `ConnectionStrings__Default` (Neon connection string) and `Admin__Password` in the service env vars.
- Dockerfile bundles the official Stockfish 17.1 Ubuntu binary into the image.
- Front with Cloudflare (free plan): add the site, set an A record to Render's IP and enable the proxy (orange cloud). SignalR over HTTPS works through the proxy with WebSockets enabled by default.

## Hub overview

Client methods: `GetOnlineUsers`, `GetLobbyGames`, `CreateChallenge`, `CancelChallenge`, `DeclineChallenge`, `AcceptChallenge`, `JoinGame`, `SpectateGame`, `PlayMove`, `Resign`, `OfferDraw`, `AcceptDraw`, `DeclineDraw`, `RequestRematch`, `JoinTournament`, `LeaveTournament`.

Server events: `Presence`, `LobbyRefresh`, `ChallengeReceived`, `GameStarted`, `MovePlayed`, `ClockTick`, `GameOver`, `DrawOffered`, `RematchRequested`, `RematchStarted`, `PlayerDisconnected`, `SpectatorsChanged`, `TournamentUpdate`.

Games are server-authoritative: every move is validated against a ChessDotNetCore engine instance and persisted to the `GameMoves` table; clocks tick server-side.