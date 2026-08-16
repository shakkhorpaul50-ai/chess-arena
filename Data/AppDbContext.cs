using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models;

namespace WebApplication1.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Friendship> Friendships => Set<Friendship>();

    public DbSet<Game> Games => Set<Game>();

    public DbSet<GameMove> GameMoves => Set<GameMove>();

    public DbSet<Tournament> Tournaments => Set<Tournament>();

    public DbSet<TournamentPlayer> TournamentPlayers => Set<TournamentPlayer>();

    public DbSet<TournamentRound> TournamentRounds => Set<TournamentRound>();

    public DbSet<TournamentMatch> TournamentMatches => Set<TournamentMatch>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(32).IsRequired();
            e.HasIndex(u => u.DisplayName).IsUnique();
        });

        builder.Entity<Friendship>(e =>
        {
            e.HasOne(f => f.Requester)
                .WithMany()
                .HasForeignKey(f => f.RequesterId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(f => f.Addressee)
                .WithMany()
                .HasForeignKey(f => f.AddresseeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();
        });

        builder.Entity<Game>(e =>
        {
            e.HasIndex(g => g.GameKey).IsUnique();
            e.HasIndex(g => g.Status);
            e.HasIndex(g => g.Mode);

            e.HasOne(g => g.WhitePlayer)
                .WithMany()
                .HasForeignKey(g => g.WhitePlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(g => g.BlackPlayer)
                .WithMany()
                .HasForeignKey(g => g.BlackPlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(g => g.TournamentMatch)
                .WithOne(tm => tm.Game)
                .HasForeignKey<Game>(g => g.TournamentMatchId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<GameMove>(e =>
        {
            e.HasOne(m => m.Game)
                .WithMany(g => g.Moves)
                .HasForeignKey(m => m.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(m => new { m.GameId, m.MoveNumber }).IsUnique();
        });

        builder.Entity<Tournament>(e =>
        {
            e.HasOne(t => t.CreatedBy)
                .WithMany()
                .HasForeignKey(t => t.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TournamentPlayer>(e =>
        {
            e.HasOne(tp => tp.Tournament)
                .WithMany(t => t.Players)
                .HasForeignKey(tp => tp.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(tp => tp.Player)
                .WithMany()
                .HasForeignKey(tp => tp.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(tp => new { tp.TournamentId, tp.PlayerId }).IsUnique();
        });

        builder.Entity<TournamentRound>(e =>
        {
            e.HasOne(r => r.Tournament)
                .WithMany(t => t.Rounds)
                .HasForeignKey(r => r.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TournamentMatch>(e =>
        {
            e.HasOne(m => m.Round)
                .WithMany(r => r.Matches)
                .HasForeignKey(m => m.RoundId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(m => m.Tournament)
                .WithMany()
                .HasForeignKey(m => m.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(m => m.WhitePlayer)
                .WithMany()
                .HasForeignKey(m => m.WhitePlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(m => m.BlackPlayer)
                .WithMany()
                .HasForeignKey(m => m.BlackPlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(m => new { m.RoundId, m.WhitePlayerId, m.BlackPlayerId }).IsUnique();
        });
    }
}