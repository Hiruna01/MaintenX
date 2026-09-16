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
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<ServiceRecord> ServiceRecords => Set<ServiceRecord>();
    public DbSet<AgentWorkflow> AgentWorkflows => Set<AgentWorkflow>();
    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();
    public DbSet<ClarificationQuestion> ClarificationQuestions => Set<ClarificationQuestion>();
    public DbSet<ClarificationAnswer> ClarificationAnswers => Set<ClarificationAnswer>();

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

        modelBuilder.Entity<AssetCategory>(entity =>
        {
            entity.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<Asset>(entity =>
        {
            // The QR payload. Unique across the estate, because a scan yields nothing but
            // this string and must identify exactly one piece of equipment.
            entity.HasIndex(a => a.AssetTag).IsUnique();

            // Both filters on the list endpoint, so both get an index.
            entity.HasIndex(a => a.RoomId);
            entity.HasIndex(a => a.AssetCategoryId);

            // Same reasoning as Role and WorkflowState: the database reads "UnderMaintenance",
            // not "1", and inserting a new enum member in the middle cannot silently
            // re-label existing rows.
            entity.Property(a => a.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            // Restrict, not Cascade: an asset carries a service history the diagnostic
            // agent reads, so deleting the room or category out from under it must fail
            // loudly rather than quietly taking that history with it. Equipment that
            // leaves the estate is marked Retired, not deleted.
            entity.HasOne(a => a.Category)
                  .WithMany(c => c.Assets)
                  .HasForeignKey(a => a.AssetCategoryId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.Room)
                  .WithMany()
                  .HasForeignKey(a => a.RoomId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ServiceRecord>(entity =>
        {
            // Every read of an asset's history filters by this column.
            entity.HasIndex(s => s.AssetId);

            // And the agent reads history as a time series — "what happened to this
            // machine, in order", and "what was serviced in this period".
            entity.HasIndex(s => s.ServicedOn);

            entity.Property(s => s.Outcome)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            // Restrict for the same reason as above: history outlives the working life of
            // the machine it describes.
            entity.HasOne(s => s.Asset)
                  .WithMany(a => a.ServiceRecords)
                  .HasForeignKey(s => s.AssetId)
                  .OnDelete(DeleteBehavior.Restrict);

            // WorkOrderId is intentionally a plain int? with no foreign key: the WorkOrder
            // table does not exist yet (Component C). It becomes a real key then.
        });

        modelBuilder.Entity<Report>(entity =>
        {
            // Same reasoning as Role and WorkflowState: the database reads "Submitted",
            // not "0", so a manual query during a demo is readable and inserting a new
            // enum member in the middle cannot silently re-label existing rows.
            entity.Property(r => r.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            entity.HasIndex(r => r.RoomId);

            // Restrict, not Cascade: a report is the head of an audit trail (its workflow
            // and that workflow's steps), so deleting the room or the user out from under
            // it must fail loudly rather than quietly taking the trail with it.
            entity.HasOne(r => r.Room)
                  .WithMany()
                  .HasForeignKey(r => r.RoomId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Reporter)
                  .WithMany()
                  .HasForeignKey(r => r.ReporterId)
                  .OnDelete(DeleteBehavior.Restrict);

            // Nullable, so most reports name no asset. Restrict for the same reason as
            // the two above, and because an asset carries the service history the
            // diagnostic agent reads — it must not be deletable out from under a report
            // that blames it.
            entity.HasOne(r => r.Asset)
                  .WithMany()
                  .HasForeignKey(r => r.AssetId)
                  .OnDelete(DeleteBehavior.Restrict);
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

            entity.HasIndex(w => w.ReportId);

            // A real foreign key now that Report exists. No navigation property on either
            // side: nothing reads a workflow through its report or vice versa, and adding
            // one would only invite a lazy include. Restrict for the same reason as above
            // — a workflow must not outlive the report it exists to explain, and it must
            // not be silently deleted with it either.
            entity.HasOne<Report>()
                  .WithMany()
                  .HasForeignKey(w => w.ReportId)
                  .OnDelete(DeleteBehavior.Restrict);
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

        modelBuilder.Entity<ClarificationQuestion>(entity =>
        {
            // Both are read paths in their own right: "what was this report asked?" and
            // "what did this run ask?", so both get an index.
            entity.HasIndex(q => q.ReportId);
            entity.HasIndex(q => q.WorkflowId);

            // Same reasoning as Role and WorkflowState: the column reads "SingleSelect",
            // not "1", and inserting a new member in the middle of the enum cannot
            // silently re-label the rows already stored.
            entity.Property(q => q.AnswerType)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(q => q.OptionsJson).HasColumnType(JsonColumnType);

            // Restrict on both, matching Report's own foreign keys: a question is part of
            // the record of what was asked about a fault, so deleting the report or the
            // run out from under it must fail loudly rather than quietly taking it along.
            entity.HasOne(q => q.Report)
                  .WithMany(r => r.ClarificationQuestions)
                  .HasForeignKey(q => q.ReportId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(q => q.Workflow)
                  .WithMany()
                  .HasForeignKey(q => q.WorkflowId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClarificationAnswer>(entity =>
        {
            // ONE answer per question, enforced by the database rather than by whichever
            // service happens to write one. A unique index is the whole "at most one"
            // rule: a second insert for the same question fails instead of leaving two
            // rows for a reader to pick between. Note that the EF in-memory provider does
            // not enforce unique indexes, which is exactly why the tests never use it.
            entity.HasIndex(a => a.ClarificationQuestionId).IsUnique();

            // One caveat worth knowing before writing the submit-answers endpoint: inside a
            // single DbContext that has ALREADY LOADED the existing answer, EF resolves the
            // conflict itself rather than letting the database see it — the relationship is
            // a required one-to-one, so the old dependent is marked Deleted and the save
            // succeeds by REPLACING the answer. The index is what stops a writer that only
            // knows a question id. A service that means "reject a re-answer" has to check
            // for one and say so; it will not get an exception for free.

            // Cascade, unlike everything else here: an answer says nothing on its own —
            // without its question there is no way to know what it answers — so it cannot
            // meaningfully outlive one. Questions are not deleted in practice; this only
            // says what would happen if one ever were.
            entity.HasOne(a => a.Question)
                  .WithOne(q => q.Answer)
                  .HasForeignKey<ClarificationAnswer>(a => a.ClarificationQuestionId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.AnsweredBy)
                  .WithMany()
                  .HasForeignKey(a => a.AnsweredByUserId)
                  .OnDelete(DeleteBehavior.Restrict);
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
            // Every timestamped entity must be named here. A new one that is left off
            // this list compiles, runs, and silently keeps CreatedAt/UpdatedAt at default.
            if (entry.Entity is not (User or Building or Room or Report
                or AssetCategory or Asset or ServiceRecord
                or AgentWorkflow or AgentStep
                or ClarificationQuestion or ClarificationAnswer))
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
