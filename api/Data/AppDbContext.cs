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
    public DbSet<AgentWorkflow> AgentWorkflows => Set<AgentWorkflow>();
    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();

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

        modelBuilder.Entity<AgentWorkflow>(entity =>
        {
            // Same reasoning as Role: the database reads "AwaitingManagerApproval", not "4",
            // so a manual query during a demo is readable and inserting a new enum member
            // in the middle cannot silently re-label existing rows.
            entity.Property(w => w.CurrentState)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            // The list endpoint filters on state, so it gets an index.
            entity.HasIndex(w => w.CurrentState);

            entity.Property(w => w.PlanJson).HasColumnType(JsonColumnType);
        });

        modelBuilder.Entity<AgentStep>(entity =>
        {
            // Every read of a workflow's steps filters by this column.
            entity.HasIndex(s => s.WorkflowId);

            entity.HasOne(s => s.Workflow)
                  .WithMany(w => w.Steps)
                  .HasForeignKey(s => s.WorkflowId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(s => s.ToolCallsJson).HasColumnType(JsonColumnType);
            entity.Property(s => s.PayloadJson).HasColumnType(JsonColumnType);
        });
    }

    /// <summary>
    /// jsonb on PostgreSQL — the real target — so plans, tool calls and payloads are
    /// queryable and validated as JSON by the database rather than being opaque text.
    /// SQLite (integration tests only) has no jsonb type, so those same columns fall back
    /// to TEXT there; nothing in the application reads them as anything but a string, so
    /// the two behave identically from C#'s point of view.
    /// </summary>
    private string JsonColumnType => Database.IsNpgsql() ? "jsonb" : "TEXT";

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
            if (entry.Entity is not (User or Building or Room or AgentWorkflow or AgentStep))
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
