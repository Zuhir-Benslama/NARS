using Microsoft.EntityFrameworkCore;
using NarsApi.Models;

namespace NarsApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Feature> Features { get; set; }
    public DbSet<Wilaya> Wilayas { get; set; }
    public DbSet<Daira> Dairas { get; set; }
    public DbSet<Commune> Communes { get; set; }
    public DbSet<CommuneBoundary> CommuneBoundaries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Unique indexes
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username).IsUnique();
    }
}
