using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
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
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<ScheduledSlot> ScheduledSlots => Set<ScheduledSlot>();
    public DbSet<ClassScheduleSlot> ClassScheduleSlots => Set<ClassScheduleSlot>();
    public DbSet<VerificationCheck> VerificationChecks => Set<VerificationCheck>();

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

            // WorkOrderId was a plain int? with no foreign key while the WorkOrder table
            // did not exist. It exists now, so this is a real key — with no navigation
            // property on either side, the same as AgentWorkflow -> Report: nothing reads
            // a record through its order or an order through its records, and adding one
            // would only invite a lazy include.
            //
            // Restrict, like every other key into the registry: the completed order is
            // what explains this row, so deleting it must fail loudly rather than quietly
            // taking the history with it.
            //
            // Still NULLABLE, and legitimately so: seeded rows and any history imported
            // from before the system existed were never produced by a work order. The
            // migration that added this key clears orphaned values first — see
            // AddWorkOrders.
            entity.HasOne<WorkOrder>()
                  .WithMany()
                  .HasForeignKey(s => s.WorkOrderId)
                  .OnDelete(DeleteBehavior.Restrict);
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

            // Every one of these is a read path GET /api/reports filters or orders on, so
            // every one gets an index — the same reasoning as WorkOrder's four.
            //
            // ReporterId is the one that matters most: it is not an optional filter but the
            // VISIBILITY SCOPE, applied to every list and detail read a Reporter makes, so
            // it is on the hot path of the most common request this table serves.
            entity.HasIndex(r => r.ReporterId);
            entity.HasIndex(r => r.RoomId);
            entity.HasIndex(r => r.AssetId);
            entity.HasIndex(r => r.Status);

            // The default sort, and the dateFrom/dateTo range filter.
            entity.HasIndex(r => r.CreatedAt);

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

            // The repair that did not hold. A real key, no navigation, Restrict — the same
            // shape as ServiceRecord.WorkOrderId: the order is the evidence the workflow was
            // reopened on, so deleting it must fail loudly.
            entity.HasOne<WorkOrder>()
                  .WithMany()
                  .HasForeignKey(w => w.ReopenedWorkOrderId)
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

        modelBuilder.Entity<WorkOrder>(entity =>
        {
            // All four are read paths in their own right: a report's orders, an asset's
            // orders, a technician's queue, and a manager's "what is waiting on me".
            entity.HasIndex(w => w.ReportId);
            entity.HasIndex(w => w.AssetId);
            entity.HasIndex(w => w.AssignedTechnicianId);
            entity.HasIndex(w => w.Status);

            // Same reasoning as Role and ReportStatus: the database reads "AwaitingApproval",
            // not "1", and inserting a new enum member in the middle cannot silently
            // re-label the rows already stored.
            entity.Property(w => w.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(w => w.Strategy)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            // MONEY IS decimal, AND ITS PRECISION IS STATED RATHER THAN INHERITED.
            // Left undeclared, EF maps decimal to an unqualified PostgreSQL `numeric`,
            // whose scale is whatever each value happens to arrive with — so the column
            // would silently accept 4999.999999 and hand it back to a threshold comparison
            // that is supposed to be reasoning about rupees and cents. 18,2 fixes the scale
            // at the database, which is the only place every writer has to go through.
            //
            // NOTE for the SQLite test mode: that provider has no decimal type and stores
            // these as TEXT, so a comparison or an ORDER BY translated into SQLite SQL
            // would compare them as strings. Any approval-threshold query must therefore
            // be evaluated in C# (after the rows are materialised), which is where the
            // rule belongs anyway — see ApprovalSettings.
            entity.Property(w => w.EstimatedCost).HasPrecision(18, 2);
            entity.Property(w => w.ActualCost).HasPrecision(18, 2);

            // Restrict on all four, matching every other key into the registry: a work
            // order is the record of money authorised and work carried out on a specific
            // machine for a specific fault, so deleting the report, the asset or either
            // user out from under it must fail loudly rather than quietly taking it along.
            entity.HasOne(w => w.Report)
                  .WithMany()
                  .HasForeignKey(w => w.ReportId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(w => w.Asset)
                  .WithMany()
                  .HasForeignKey(w => w.AssetId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(w => w.AssignedTechnician)
                  .WithMany()
                  .HasForeignKey(w => w.AssignedTechnicianId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(w => w.ApprovedBy)
                  .WithMany()
                  .HasForeignKey(w => w.ApprovedByUserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ScheduledSlot>(entity =>
        {
            // Cascade, unlike most of this model and for the same reason as
            // ClarificationAnswer: a booking says nothing without the work order it books
            // time for, so it cannot meaningfully outlive one. Work orders are not deleted
            // in practice — Cancelled is how one ends without being carried out — so this
            // only states what would happen if one ever were.
            entity.HasOne(s => s.WorkOrder)
                  .WithMany(w => w.ScheduledSlots)
                  .HasForeignKey(s => s.WorkOrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClassScheduleSlot>(entity =>
        {
            // The conflict check reads "what is on in this room, around this time", so
            // both halves of that question get an index.
            entity.HasIndex(c => c.RoomId);
            entity.HasIndex(c => c.StartsAt);

            // Unique, and that is what makes a re-sync idempotent rather than additive:
            // the same class pulled twice updates its row instead of appearing as a second
            // lecture in the same room at the same time — which would read as a conflict
            // that does not exist and push maintenance out of a room that was free.
            entity.HasIndex(c => c.ExternalEventId).IsUnique();

            // Restrict, matching Asset and Report: deleting a room out from under its
            // timetable must fail loudly rather than quietly leaving the room looking
            // permanently free.
            entity.HasOne(c => c.Room)
                  .WithMany()
                  .HasForeignKey(c => c.RoomId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VerificationCheck>(entity =>
        {
            // "What has been asked about this repair?"
            entity.HasIndex(v => v.WorkOrderId);

            // THE SWEEP'S QUERY, AND THE REASON THIS IS COMPOSITE RATHER THAN TWO INDEXES.
            // It asks for Pending checks whose DueAt has passed — equality on the first
            // column, a range on the second — which is exactly the shape a composite index
            // serves. Two separate indexes would make the database pick one and filter the
            // rest by hand.
            //
            // Status leads because it is the equality test; a range column first would
            // leave the equality unable to use the index. Being leftmost also means this
            // one index answers "everything Pending" on its own, so no separate index on
            // Status is needed.
            entity.HasIndex(v => new { v.Status, v.DueAt });

            // The agent's cited evidence, verbatim, in the same jsonb type as every other
            // column holding what an agent produced.
            entity.Property(v => v.AgentEvidenceJson).HasColumnType(JsonColumnType);

            // Same reasoning as Role and WorkOrderStatus: the database reads
            // "AwaitingReporterResponse", not "1", and inserting a new enum member in the
            // middle cannot silently re-label the rows already stored.
            entity.Property(v => v.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50)
                  .IsRequired();

            // Restrict on both, matching every other key into the registry: a check is the
            // record of whether a repair held, so deleting the work order or the asset out
            // from under it must fail loudly rather than quietly taking the evidence along.
            //
            // No navigation back from WorkOrder: nothing reads an order through its checks,
            // and a collection there would only invite a lazy include on the order's own
            // detail read.
            entity.HasOne(v => v.WorkOrder)
                  .WithMany()
                  .HasForeignKey(v => v.WorkOrderId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(v => v.Asset)
                  .WithMany()
                  .HasForeignKey(v => v.AssetId)
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
        CheckWorkflowTransitions();
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        CheckWorkflowTransitions();
        ApplyTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// The state machine's backstop. Every service moves a workflow through
    /// WorkflowTransitions.Move, which throws before anything else happens; this refuses the
    /// save for any changed CurrentState that did not — a direct assignment somebody added
    /// later — so "no state changes anywhere else" is enforced rather than hoped for.
    ///
    /// It compares the value loaded from the database with the one being written, so a unit
    /// of work moves a workflow at most ONE step per save; two Moves before one save would be
    /// checked as a single jump, and the runner saves after each for that reason. A NEW row is
    /// not a transition and is not checked: StartAsync creates every workflow as Submitted,
    /// and the demo seeder writes its rows where a real run would have left them.
    /// </summary>
    private void CheckWorkflowTransitions()
    {
        foreach (var entry in ChangeTracker.Entries<AgentWorkflow>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var state = entry.Property(w => w.CurrentState);

            if (state.IsModified
                && state.OriginalValue != state.CurrentValue
                && !WorkflowTransitions.CanReach(state.OriginalValue, state.CurrentValue))
            {
                throw new InvalidWorkflowTransitionException(
                    entry.Entity.Id, state.OriginalValue, state.CurrentValue);
            }
        }
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
                or ClarificationQuestion or ClarificationAnswer
                or WorkOrder or ScheduledSlot or ClassScheduleSlot
                or VerificationCheck))
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

                // A re-sync that finds a class unchanged still stamps SyncedAt, and that alone
                // is not an update: UpdatedAt says when the CLASS last changed, SyncedAt says
                // when it was last confirmed. Bumping both would make them the same column.
                if (entry.Entity is ClassScheduleSlot
                    && entry.Properties.Where(p => p.IsModified)
                        .All(p => p.Metadata.Name == nameof(ClassScheduleSlot.SyncedAt)))
                {
                    continue;
                }

                entry.Property(nameof(User.UpdatedAt)).CurrentValue = now;
            }
        }
    }
}
