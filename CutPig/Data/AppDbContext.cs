using CutPig.Domain;
using Microsoft.EntityFrameworkCore;

namespace CutPig.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<GamePlayer> GamePlayers => Set<GamePlayer>();
    public DbSet<GameRound> GameRounds => Set<GameRound>();
    public DbSet<RoundResult> RoundResults => Set<RoundResult>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomSeat> RoomSeats => Set<RoomSeat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<Game>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasMany(x => x.Players).WithOne(x => x.Game!).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Rounds).WithOne(x => x.Game!).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GamePlayer>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.GameId, x.Seat }).IsUnique();
            b.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GameRound>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.GameId, x.RoundNumber }).IsUnique();
            b.HasMany(x => x.Results).WithOne(x => x.GameRound!).HasForeignKey(x => x.GameRoundId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RoundResult>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppUser>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<AuthToken>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Token).IsUnique();
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Room>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasOne(x => x.HostUser).WithMany().HasForeignKey(x => x.HostUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Seats).WithOne(x => x.Room!).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RoomSeat>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.RoomId, x.SeatIndex }).IsUnique();
            b.HasIndex(x => new { x.RoomId, x.UserId }).IsUnique();
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
