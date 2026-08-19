using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Room> Rooms => Set<Room>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();

            // Persist Role as a string, not the default int, so the database reads
            // "FacilitiesManager" instead of "2" during a demo or a manual query.
            entity.Property(u => u.Role)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();
        });

        modelBuilder.Entity<Building>(entity =>
        {
            entity.HasIndex(b => b.Code).IsUnique();
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasIndex(r => r.BuildingId);

            entity.HasOne(r => r.Building)
                  .WithMany(b => b.Rooms)
                  .HasForeignKey(r => r.BuildingId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// CreatedAt/UpdatedAt are maintained here so no service or controller has to
    /// remember to set them. UTC everywhere — Npgsql maps DateTime to timestamptz.
    /// </summary>
    private void ApplyTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not (User or Building or Room))
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(User.CreatedAt)).CurrentValue = now;
                entry.Property(nameof(User.UpdatedAt)).CurrentValue = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(User.CreatedAt)).IsModified = false;
                entry.Property(nameof(User.UpdatedAt)).CurrentValue = now;
            }
        }
    }
}
