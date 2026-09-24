using System.Text.Json;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Data;

/// <summary>
/// Development-only demo data. Idempotent: every insert is guarded by a check on the
/// natural key, so running it on an already-seeded database is a no-op.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        IConfiguration configuration,
        IPasswordHasher<User> passwordHasher,
        VerificationSettings verificationSettings,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await SeedBuildingsAndRoomsAsync(db, cancellationToken);
        await SeedAssetRegistryAsync(db, cancellationToken);

        // Users before work orders: a work order hangs off a report, and a report needs a
        // reporter. The verification seeder checks for one and backs out if it is absent.
        await SeedUsersAsync(db, configuration, passwordHasher, logger, cancellationToken);
        await SeedVerificationAsync(db, verificationSettings, logger, cancellationToken);
        await SeedLiveWorkOrdersAsync(db, logger, cancellationToken);
    }

    private static async Task SeedBuildingsAndRoomsAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var buildings = new[]
        {
            new Building { Name = "Main Academic Block", Code = "MAB" },
            new Building { Name = "Engineering Faculty", Code = "ENG" }
        };

        foreach (var building in buildings)
        {
            var exists = await db.Buildings.AnyAsync(b => b.Code == building.Code, cancellationToken);
            if (!exists)
            {
                db.Buildings.Add(building);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var mabId = await db.Buildings.Where(b => b.Code == "MAB")
                                      .Select(b => b.Id)
                                      .SingleAsync(cancellationToken);
        var engId = await db.Buildings.Where(b => b.Code == "ENG")
                                      .Select(b => b.Id)
                                      .SingleAsync(cancellationToken);

        var rooms = new[]
        {
            new Room { BuildingId = mabId, Name = "Lecture Hall A", Code = "MAB-101", Floor = 1 },
            new Room { BuildingId = mabId, Name = "Lecture Hall B", Code = "MAB-102", Floor = 1 },
            new Room { BuildingId = mabId, Name = "Seminar Room 1", Code = "MAB-201", Floor = 2 },
            new Room { BuildingId = engId, Name = "Computer Lab 1", Code = "ENG-101", Floor = 1 },
            new Room { BuildingId = engId, Name = "Computer Lab 2", Code = "ENG-102", Floor = 1 },
            new Room { BuildingId = engId, Name = "Electronics Lab", Code = "ENG-301", Floor = 3 }
        };

        foreach (var room in rooms)
        {
            var exists = await db.Rooms.AnyAsync(r => r.Code == room.Code, cancellationToken);
            if (!exists)
            {
                db.Rooms.Add(room);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Categories, assets and service history.
    ///
    /// THIS DATA IS LOAD-BEARING, NOT FILLER. The diagnostic agent is written against it:
    /// it has to find repeat failures by reading across several service records, and it
    /// cannot be developed or demonstrated without history that actually contains one.
    /// The notes are written the way technicians write them — terse, abbreviated, and
    /// vague where a real note would be vague — because the agent's job is to read
    /// unstructured text, and clean prose here would make it look better than it is.
    ///
    /// The three records on PRJ-MAB101-01 are the planted pattern: one visit finding
    /// nothing, then the same fault returning twice with the same thermal root cause and
    /// two temporary fixes, escalating over four months to a replacement recommendation.
    /// Read individually each note is unremarkable; read together they are a failing unit.
    /// Do not "tidy" them into one clean record.
    /// </summary>
    private static async Task SeedAssetRegistryAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var categories = new[]
        {
            new AssetCategory { Name = "Projector", DefaultWarrantyMonths = 24 },
            new AssetCategory { Name = "Air Conditioner", DefaultWarrantyMonths = 36 },
            new AssetCategory { Name = "Water Pump", DefaultWarrantyMonths = 12 },
            new AssetCategory { Name = "Lab Workstation", DefaultWarrantyMonths = 36 }
        };

        foreach (var category in categories)
        {
            var exists = await db.AssetCategories.AnyAsync(c => c.Name == category.Name, cancellationToken);
            if (!exists)
            {
                db.AssetCategories.Add(category);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var categoryIds = await db.AssetCategories
            .ToDictionaryAsync(c => c.Name, c => c.Id, cancellationToken);

        var roomIds = await db.Rooms
            .ToDictionaryAsync(r => r.Code, r => r.Id, cancellationToken);

        // Tag format is TYPE-ROOM-SEQ, matching what is printed on the sticker and encoded
        // in the QR code on the equipment itself.
        var assets = new[]
        {
            new Asset
            {
                AssetTag = "PRJ-MAB101-01",
                Name = "Lecture Hall A Projector",
                AssetCategoryId = categoryIds["Projector"],
                RoomId = roomIds["MAB-101"],
                Manufacturer = "Epson",
                Model = "EB-990U",
                InstalledOn = new DateOnly(2023, 8, 14),
                WarrantyExpiresOn = new DateOnly(2025, 8, 14),
                // Still Active, and that is the point: it works between failures, so
                // nothing about the asset row itself looks wrong. The pattern is only in
                // the history.
                Status = AssetStatus.Active
            },
            new Asset
            {
                AssetTag = "PRJ-MAB102-01",
                Name = "Lecture Hall B Projector",
                AssetCategoryId = categoryIds["Projector"],
                RoomId = roomIds["MAB-102"],
                Manufacturer = "Epson",
                Model = "EB-980W",
                InstalledOn = new DateOnly(2024, 1, 22),
                WarrantyExpiresOn = new DateOnly(2026, 1, 22),
                Status = AssetStatus.Active
            },
            new Asset
            {
                AssetTag = "PRJ-MAB201-01",
                Name = "Seminar Room 1 Projector",
                AssetCategoryId = categoryIds["Projector"],
                RoomId = roomIds["MAB-201"],
                Manufacturer = "BenQ",
                Model = "MX560",
                InstalledOn = new DateOnly(2021, 6, 30),
                // No warranty recorded — an older unit that predates the registry.
                WarrantyExpiresOn = null,
                Status = AssetStatus.Retired
            },
            new Asset
            {
                AssetTag = "ACU-MAB101-01",
                Name = "Lecture Hall A Split AC",
                AssetCategoryId = categoryIds["Air Conditioner"],
                RoomId = roomIds["MAB-101"],
                Manufacturer = "Daikin",
                Model = "FTKF50TV",
                InstalledOn = new DateOnly(2024, 3, 5),
                WarrantyExpiresOn = new DateOnly(2027, 3, 5),
                Status = AssetStatus.Active
            },
            new Asset
            {
                AssetTag = "ACU-ENG101-01",
                Name = "Computer Lab 1 Split AC",
                AssetCategoryId = categoryIds["Air Conditioner"],
                RoomId = roomIds["ENG-101"],
                Manufacturer = "Mitsubishi Electric",
                Model = "MSY-GN18VF",
                InstalledOn = new DateOnly(2023, 11, 18),
                WarrantyExpiresOn = new DateOnly(2026, 11, 18),
                Status = AssetStatus.UnderMaintenance
            },
            new Asset
            {
                AssetTag = "PMP-ENG301-01",
                Name = "Electronics Lab Water Pump",
                AssetCategoryId = categoryIds["Water Pump"],
                RoomId = roomIds["ENG-301"],
                Manufacturer = "Grundfos",
                Model = "CM3-4",
                InstalledOn = new DateOnly(2022, 9, 9),
                WarrantyExpiresOn = new DateOnly(2023, 9, 9),
                Status = AssetStatus.Active
            },
            new Asset
            {
                AssetTag = "WKS-ENG101-01",
                Name = "Computer Lab 1 Workstation 01",
                AssetCategoryId = categoryIds["Lab Workstation"],
                RoomId = roomIds["ENG-101"],
                Manufacturer = "Dell",
                Model = "Precision 3660",
                InstalledOn = new DateOnly(2025, 2, 10),
                WarrantyExpiresOn = new DateOnly(2028, 2, 10),
                Status = AssetStatus.Active
            },
            new Asset
            {
                AssetTag = "WKS-ENG102-01",
                Name = "Computer Lab 2 Workstation 01",
                AssetCategoryId = categoryIds["Lab Workstation"],
                RoomId = roomIds["ENG-102"],
                Manufacturer = "HP",
                Model = "Z2 G9 Tower",
                InstalledOn = new DateOnly(2025, 2, 10),
                WarrantyExpiresOn = new DateOnly(2028, 2, 10),
                Status = AssetStatus.Active
            }
        };

        foreach (var asset in assets)
        {
            var exists = await db.Assets.AnyAsync(a => a.AssetTag == asset.AssetTag, cancellationToken);
            if (!exists)
            {
                db.Assets.Add(asset);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var assetIds = await db.Assets
            .ToDictionaryAsync(a => a.AssetTag, a => a.Id, cancellationToken);

        var history = new[]
        {
            // ---------------------------------------------------------------------
            // PRJ-MAB101-01 — THE PLANTED REPEAT FAILURE. Four months, three visits,
            // one underlying thermal fault that nobody fixed. Keep all three.
            // ---------------------------------------------------------------------
            (Tag: "PRJ-MAB101-01", On: new DateOnly(2026, 5, 12), Tech: "K. Perera",
                Note: "projector cutting out mid lecture. checked hdmi + cable, reseated both. ran 20min on test, no fault seen. adv. dept to report again if recurs.",
                Outcome: ServiceOutcome.NoFaultFound),
            (Tag: "PRJ-MAB101-01", On: new DateOnly(2026, 7, 3), Tech: "K. Perera",
                Note: "same complaint as May. air filter choked w/ dust, lamp hrs high. cleaned filter, ok on test after 30min.",
                Outcome: ServiceOutcome.TemporaryFix),
            (Tag: "PRJ-MAB101-01", On: new DateOnly(2026, 9, 2), Tech: "S. Fernando",
                Note: "cleaned filter, unit still running hot, temporary fix, fan bearing sounds weak - recommend replacement before next term",
                Outcome: ServiceOutcome.TemporaryFix),

            // --- PRJ-MAB102-01 -------------------------------------------------
            (Tag: "PRJ-MAB102-01", On: new DateOnly(2026, 2, 18), Tech: "M. Silva",
                Note: "lamp replaced at 2040 hrs. brightness + colour ok after.",
                Outcome: ServiceOutcome.PartReplaced),
            (Tag: "PRJ-MAB102-01", On: new DateOnly(2026, 8, 21), Tech: "M. Silva",
                Note: "remote not working. batteries flat, replaced. nothing wrong w/ unit.",
                Outcome: ServiceOutcome.Resolved),

            // --- PRJ-MAB201-01 -------------------------------------------------
            (Tag: "PRJ-MAB201-01", On: new DateOnly(2025, 11, 7), Tech: "K. Perera",
                Note: "no power at all. psu board burnt out. out of warranty + parts not avail locally, not econ to repair. marked for retirement.",
                Outcome: ServiceOutcome.Resolved),

            // --- ACU-MAB101-01 -------------------------------------------------
            (Tag: "ACU-MAB101-01", On: new DateOnly(2026, 1, 15), Tech: "A. Jayasuriya",
                Note: "routine service. gas pressure ok, filters washed, drain clear.",
                Outcome: ServiceOutcome.Resolved),
            (Tag: "ACU-MAB101-01", On: new DateOnly(2026, 6, 30), Tech: "A. Jayasuriya",
                Note: "not cooling properly. topped up refrigerant. possible slow leak, monitor.",
                Outcome: ServiceOutcome.TemporaryFix),

            // --- ACU-ENG101-01 -------------------------------------------------
            (Tag: "ACU-ENG101-01", On: new DateOnly(2026, 3, 22), Tech: "A. Jayasuriya",
                Note: "water dripping from indoor unit onto desk. drain pipe blocked, flushed out.",
                Outcome: ServiceOutcome.Resolved),
            (Tag: "ACU-ENG101-01", On: new DateOnly(2026, 9, 8), Tech: "R. Bandara",
                Note: "compressor not starting. replaced start capacitor. still cutting in + out intermittently, unit left off pending parts.",
                Outcome: ServiceOutcome.PartReplaced),

            // --- PMP-ENG301-01 -------------------------------------------------
            (Tag: "PMP-ENG301-01", On: new DateOnly(2025, 12, 2), Tech: "R. Bandara",
                Note: "pump running dry, no suction. primed + checked foot valve. ok now.",
                Outcome: ServiceOutcome.Resolved),
            (Tag: "PMP-ENG301-01", On: new DateOnly(2026, 4, 19), Tech: "R. Bandara",
                Note: "bearing noisy. greased + re-tensioned coupling. monitor.",
                Outcome: ServiceOutcome.TemporaryFix),

            // --- Workstations --------------------------------------------------
            (Tag: "WKS-ENG101-01", On: new DateOnly(2026, 2, 5), Tech: "M. Silva",
                Note: "wont boot, no display. reseated ram module. booted fine after, memtest 1 pass clean.",
                Outcome: ServiceOutcome.Resolved),
            (Tag: "WKS-ENG102-01", On: new DateOnly(2026, 7, 14), Tech: "S. Fernando",
                Note: "bsod under load during lab session. replaced psu (450w -> 550w). stable since.",
                Outcome: ServiceOutcome.PartReplaced)
        };

        foreach (var entry in history)
        {
            var assetId = assetIds[entry.Tag];

            // Guarded on asset + date, which is unique across this data set. A service
            // record has no natural key of its own — that is the honest consequence of it
            // being an append-only historical log rather than a row anyone looks up.
            var exists = await db.ServiceRecords.AnyAsync(
                s => s.AssetId == assetId && s.ServicedOn == entry.On, cancellationToken);

            if (exists)
            {
                continue;
            }

            db.ServiceRecords.Add(new ServiceRecord
            {
                AssetId = assetId,
                ServicedOn = entry.On,
                TechnicianName = entry.Tech,
                TechnicianNote = entry.Note,
                Outcome = entry.Outcome,
                // No work order: these predate Component C, and history imported from
                // before the system existed never has one.
                WorkOrderId = null
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Completed work orders and the verification checks raised against them.
    ///
    /// THIS DATA IS LOAD-BEARING TOO. The verification sweep and the metrics endpoint
    /// cannot be developed or demonstrated against an empty table, and they cannot be
    /// developed against a table where every check says the same thing — a confirmation
    /// rate of 100% looks identical whether the code is right or the query is wrong.
    /// So the seeded set deliberately contains an answer of each kind, plus three checks
    /// that are already overdue so the sweep has something to pick up the first time it
    /// runs.
    ///
    /// The Reopened one sits on PRJ-MAB101-01 ON PURPOSE. That is the projector whose
    /// service history carries the planted repeat-failure pattern, so the reopened check
    /// is the same fault surfacing once more — this time caught by the verification loop
    /// rather than by a fourth person reporting it. An escalation rule written against
    /// this data has a real case to find rather than an invented one.
    ///
    /// Idempotent like the rest of the seeder: each entry is guarded on its report's
    /// description, which is unique across this data set. A work order has no natural key
    /// of its own — the honest consequence of it being a row somebody raises rather than a
    /// thing with a name — so the report it hangs off is what identifies the triple.
    /// </summary>
    private static async Task SeedVerificationAsync(
        AppDbContext db,
        VerificationSettings verificationSettings,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Every seeded work order hangs off a report, and Report.ReporterId is not
        // nullable. If demo users were skipped because no passwords are configured, there
        // is nobody to file them — so this backs out with a warning rather than failing
        // the whole seed run, the same way a missing password skips one user.
        var reporterId = await db.Users
            .Where(u => u.Email == "reporter@campus.test")
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (reporterId is null)
        {
            logger.LogWarning(
                "Skipping work order and verification seeding: no demo reporter exists. " +
                "Configure Seed:Passwords:Reporter and run again.");
            return;
        }

        // Both nullable on WorkOrder, so a missing demo user leaves the column null rather
        // than blocking the seed.
        var technicianId = await db.Users
            .Where(u => u.Email == "technician@campus.test")
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var managerId = await db.Users
            .Where(u => u.Email == "manager@campus.test")
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var assets = await db.Assets
            .Select(a => new { a.Id, a.AssetTag, a.RoomId })
            .ToDictionaryAsync(a => a.AssetTag, a => a, cancellationToken);

        var now = DateTime.UtcNow;

        // Completion dates run from 6 to 20 days ago. The nearest is six rather than two
        // because DueAt is completion plus the configured delay (5 days by default): a work
        // order finished two days ago is not yet due, and could not be one of the three
        // overdue checks this data has to provide. See the note on DaysAgoCompleted.
        var seeds = new[]
        {
            // --- Confirmed: the repair held ------------------------------------
            new VerificationSeed(
                AssetTag: "PRJ-MAB102-01",
                DaysAgoCompleted: 20,
                Strategy: WorkOrderStrategy.KnownFix,
                EstimatedCost: 8_500m,
                ActualCost: 8_500m,
                Approved: false,
                ReportDescription: "Projector in Lecture Hall B shows no picture, just a blue screen, since Monday.",
                ResolutionNote: "hdmi input board reseated + cable swapped. tested 30min w/ laptop and doc cam, picture stable.",
                CheckStatus: VerificationStatus.Confirmed,
                DaysAgoResponded: 14,
                ReporterComment: "Working fine all week, thanks.",
                AgentOutcome: null,
                AgentReason: null),

            new VerificationSeed(
                AssetTag: "ACU-MAB101-01",
                DaysAgoCompleted: 18,
                Strategy: WorkOrderStrategy.SingleJob,
                EstimatedCost: 12_000m,
                ActualCost: 11_400m,
                Approved: false,
                ReportDescription: "AC in Lecture Hall A rattles loudly enough to drown out the lecturer.",
                ResolutionNote: "fan blade loose on spindle. tightened + rebalanced, filters washed while open. no noise on 20min test.",
                CheckStatus: VerificationStatus.Confirmed,
                DaysAgoResponded: 12,
                ReporterComment: "Much quieter now.",
                AgentOutcome: null,
                AgentReason: null),

            // --- Reopened: the repair did NOT hold -----------------------------
            // On the projector with the planted repeat-failure history. This is that same
            // thermal fault coming back a fourth time, caught here instead of by another
            // report from the room.
            new VerificationSeed(
                AssetTag: "PRJ-MAB101-01",
                DaysAgoCompleted: 15,
                Strategy: WorkOrderStrategy.SingleJob,
                EstimatedCost: 9_500m,
                ActualCost: 9_500m,
                Approved: false,
                ReportDescription: "Lecture Hall A projector keeps cutting out about ten minutes into every lecture.",
                ResolutionNote: "filter cleaned again + thermal paste redone on lamp housing. ran 40min continuous, no cutout on test.",
                CheckStatus: VerificationStatus.Reopened,
                DaysAgoResponded: 9,
                ReporterComment: "Cut out twice again this week. Same as before.",
                AgentOutcome: "escalate",
                AgentReason: "Third thermal-related intervention on this unit in five months; two prior visits recorded as temporary fixes and one as no fault found. Cleaning is not holding. Recommend replacement assessment rather than a fourth clean."),

            // --- Pending, and already overdue: the sweep's first work ----------
            new VerificationSeed(
                AssetTag: "ACU-ENG101-01",
                DaysAgoCompleted: 12,
                Strategy: WorkOrderStrategy.SingleJob,
                EstimatedCost: 22_000m,
                ActualCost: 24_500m,
                // Above the default 15,000 threshold, so a manager had to decide before
                // this one was scheduled.
                Approved: true,
                ReportDescription: "Computer Lab 1 AC is not cooling at all, the room is unusable after midday.",
                ResolutionNote: "start capacitor + contactor replaced. cooling to 24c on test, gas pressure ok.",
                CheckStatus: VerificationStatus.Pending,
                DaysAgoResponded: null,
                ReporterComment: null,
                AgentOutcome: null,
                AgentReason: null),

            new VerificationSeed(
                AssetTag: "PMP-ENG301-01",
                DaysAgoCompleted: 9,
                Strategy: WorkOrderStrategy.KnownFix,
                EstimatedCost: 6_000m,
                ActualCost: 6_000m,
                Approved: false,
                ReportDescription: "Water pump in the Electronics Lab is noisy and pressure keeps dropping.",
                ResolutionNote: "bearing replaced + coupling re-aligned. back to 2.4 bar, ran 15min no noise.",
                CheckStatus: VerificationStatus.Pending,
                DaysAgoResponded: null,
                ReporterComment: null,
                AgentOutcome: null,
                AgentReason: null),

            new VerificationSeed(
                AssetTag: "WKS-ENG101-01",
                DaysAgoCompleted: 6,
                Strategy: WorkOrderStrategy.SingleJob,
                EstimatedCost: 15_500m,
                ActualCost: 15_500m,
                // Also above the default threshold, and only just — a useful row to have
                // when checking that the comparison is > and not >=.
                Approved: true,
                ReportDescription: "Workstation 01 in Computer Lab 1 restarts by itself during lab sessions.",
                ResolutionNote: "psu replaced (450w -> 550w). stress tested 1hr under load, no restart.",
                CheckStatus: VerificationStatus.Pending,
                DaysAgoResponded: null,
                ReporterComment: null,
                AgentOutcome: null,
                AgentReason: null)
        };

        var seeded = 0;

        foreach (var seed in seeds)
        {
            if (!assets.TryGetValue(seed.AssetTag, out var asset))
            {
                logger.LogWarning(
                    "Skipping seeded work order: asset {AssetTag} does not exist.", seed.AssetTag);
                continue;
            }

            var description = seed.ReportDescription;
            if (await db.Reports.AnyAsync(r => r.Description == description, cancellationToken))
            {
                continue;
            }

            var completedAt = now.AddDays(-seed.DaysAgoCompleted);

            var report = new Report
            {
                ReporterId = reporterId.Value,
                RoomId = asset.RoomId,
                // Filled in at triage — which is exactly the path Report.AssetId exists for.
                AssetId = asset.Id,
                Description = seed.ReportDescription,
                // A confirmed repair closes the fault. A reopened or still-unanswered one
                // does not: the work order was raised and the fault is not settled yet.
                Status = seed.CheckStatus == VerificationStatus.Confirmed
                    ? ReportStatus.Closed
                    : ReportStatus.WorkOrderRaised
            };

            var workOrder = new WorkOrder
            {
                Report = report,
                AssetId = asset.Id,
                AssignedTechnicianId = technicianId,
                Status = WorkOrderStatus.Completed,
                Strategy = seed.Strategy,
                EstimatedCost = seed.EstimatedCost,
                ActualCost = seed.ActualCost,
                ResolutionNote = seed.ResolutionNote,
                CompletedAt = completedAt,
                ApprovedByUserId = seed.Approved ? managerId : null,
                ApprovedAt = seed.Approved && managerId is not null
                    ? completedAt.AddDays(-2)
                    : null
            };

            // NOTE: a real completion also appends a ServiceRecord against the asset — see
            // the note on ServiceRecord. This seeder deliberately does not, because the
            // service history on PRJ-MAB101-01 is the planted pattern the diagnostic agent
            // is written against, and quietly adding rows to it would change what that
            // agent is being developed and demonstrated against.
            var check = new VerificationCheck
            {
                WorkOrder = workOrder,
                AssetId = asset.Id,
                // The same rule the service applies: completion plus the configured delay.
                // Read from settings rather than hardcoded, so seeded rows agree with what
                // the running system would have produced.
                DueAt = completedAt.AddDays(verificationSettings.DelayDays),
                Status = seed.CheckStatus,
                // null for Pending, true for Confirmed, false for Reopened — the whole
                // reason this column is a bool? rather than a bool.
                ReporterConfirmed = seed.CheckStatus switch
                {
                    VerificationStatus.Confirmed => true,
                    VerificationStatus.Reopened => false,
                    _ => null
                },
                ReporterComment = seed.ReporterComment,
                ReporterRespondedAt = seed.DaysAgoResponded is null
                    ? null
                    : now.AddDays(-seed.DaysAgoResponded.Value),
                AgentOutcome = seed.AgentOutcome,
                AgentReason = seed.AgentReason,
                // Answered checks were asked by a sweep at some point; the still-Pending
                // ones have never been touched by one, which is what makes them its first
                // job. Leaving this null on them is the point, not an omission.
                ProcessedAt = seed.DaysAgoResponded is null
                    ? null
                    : now.AddDays(-seed.DaysAgoResponded.Value).AddHours(-1)
            };

            db.Reports.Add(report);
            db.WorkOrders.Add(workOrder);
            db.VerificationChecks.Add(check);
            seeded++;
        }

        if (seeded == 0)
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);

        var overdue = await db.VerificationChecks.CountAsync(
            v => v.Status == VerificationStatus.Pending && v.DueAt <= now, cancellationToken);

        logger.LogInformation(
            "Seeded {Count} completed work order(s) with verification checks. " +
            "{Overdue} check(s) are already due with a delay of {DelayDays} day(s).",
            seeded,
            overdue,
            verificationSettings.DelayDays);

        // A generous configured delay can push the intentionally-overdue rows into the
        // future, which would leave the sweep with nothing to do and no clue why.
        if (overdue == 0)
        {
            logger.LogWarning(
                "No seeded verification check is due yet: Verification:DelayDays is {DelayDays}, " +
                "but the most recent seeded work order completed 6 days ago. Lower the delay to " +
                "see the sweep pick anything up.",
                verificationSettings.DelayDays);
        }
    }

    /// <summary>
    /// Work orders still in flight: two AwaitingApproval, each with the agent run that
    /// preceded it, and one Approved but unassigned.
    ///
    /// LOAD-BEARING LIKE THE REST. Every other seeded order is Completed, so without these
    /// the approval queue — the screen a manager decides spending on — and the dispatch
    /// board's assign-and-schedule path would both open empty in a demo.
    ///
    /// - PRJ-MAB101-01 is the golden projector, its thermal fault back a fifth time.
    ///   EscalateReplacement at Rs 45,000: above the threshold AND a replacement, so both
    ///   halves of the gate show. Its diagnosis is the one the live eval produced against
    ///   this history — the failing cooling fan, citing the dated visits — so what the page
    ///   shows is what the agent actually says about this machine.
    /// - ACU-ENG101-01 is a SingleJob at Rs 28,000: above the threshold on cost alone. The
    ///   unit is under warranty until 2026-11-18, which the failure summary on the same card
    ///   shows — the kind of fact a manager should not have to open another page to find.
    /// - PRJ-MAB102-01 is a cheap KnownFix, auto-approved (so no ApprovedBy: nobody had to
    ///   decide) and assigned to nobody — the order to assign, find a slot for and book.
    ///
    /// THE AGENT STEPS ON THE FIRST TWO ARE SEEDED, not produced by a run, and are written
    /// in exactly the shape WorkflowRunner writes — clarifier, diagnostic, strategist, each
    /// with "[]" tool calls and the output verbatim — so the approval queue and the report's
    /// reasoning panel read them through the same code as a real run's.
    ///
    /// No ServiceRecord rows, for the same reason as the completed orders above: the
    /// planted history is what the diagnostic is developed against.
    /// </summary>
    private static async Task SeedLiveWorkOrdersAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var reporterId = await db.Users
            .Where(u => u.Email == "reporter@campus.test")
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (reporterId is null)
        {
            // Already warned about by SeedVerificationAsync; nothing more to say.
            return;
        }

        var assets = await db.Assets
            .Select(a => new { a.Id, a.AssetTag, a.RoomId })
            .ToDictionaryAsync(a => a.AssetTag, a => a, cancellationToken);

        var seeds = new[]
        {
            new LiveWorkOrderSeed(
                AssetTag: "PRJ-MAB101-01",
                ReportDescription: "Lecture Hall A projector shut itself off again in the 10am lecture. Fan was very loud for a few minutes before it went.",
                Strategy: WorkOrderStrategy.EscalateReplacement,
                EstimatedCost: 45_000m,
                PartsRequired: "Replacement projector, Epson EB-990U or equivalent (WUXGA). Existing ceiling mount reused.",
                Status: WorkOrderStatus.AwaitingApproval,
                Diagnosis: new
                {
                    hypotheses = new object[]
                    {
                        new
                        {
                            cause = "Overheating and thermal shutdown due to a failing cooling fan",
                            confidence = "high",
                            evidence = new[]
                            {
                                "2026-05-12: cutting out mid lecture, no fault found after reseating cables — consistent with an intermittent thermal cut-out rather than a signal fault.",
                                "2026-07-03: same complaint, air filter choked with dust; cleaned — recorded as a temporary fix.",
                                "2026-09-02: filter cleaned again, unit still running hot, fan bearing sounds weak; temporary fix, technician recommended replacement.",
                                "This report: fan very loud for a few minutes before the shutdown."
                            }
                        },
                        new
                        {
                            cause = "Lamp near end of life contributing to heat and shutdowns",
                            confidence = "low",
                            evidence = new[]
                            {
                                "2026-07-03: lamp hours noted as high.",
                                "No lamp replacement recorded on this unit since installation."
                            }
                        }
                    },
                    primary_hypothesis_index = 0,
                    recommended_next_action = "replace",
                    reasoning_summary = "Three visits in four months for the same cut-out. Two filter cleanings were recorded as temporary fixes and the last technician heard a weak fan bearing. Cleaning is not holding; the cooling fault keeps returning. Warranty expired 2025-08-14."
                },
                Proposal: new
                {
                    strategy = "escalate_replacement",
                    estimated_cost = 45_000.00m,
                    urgency = "high",
                    justification = "The same thermal fault has now returned after two filter cleanings recorded as temporary fixes, and the fan bearing is failing. The unit is out of warranty, so a fan and lamp repair would be paid for in full on a machine with this history. Replacing it ends the repeat visits. Lecture Hall A is used every day, so each further cut-out interrupts a lecture.",
                    consolidate_with_work_order_ids = Array.Empty<int>()
                }),

            new LiveWorkOrderSeed(
                AssetTag: "ACU-ENG101-01",
                ReportDescription: "Computer Lab 1 AC cutting in and out again since yesterday, clicking from the outdoor unit. Room too hot for the afternoon labs.",
                Strategy: WorkOrderStrategy.SingleJob,
                EstimatedCost: 28_000m,
                PartsRequired: "Outdoor unit control PCB, Mitsubishi Electric MSY-GN18VF.",
                Status: WorkOrderStatus.AwaitingApproval,
                Diagnosis: new
                {
                    hypotheses = new object[]
                    {
                        new
                        {
                            cause = "Faulty outdoor unit control board switching the compressor on and off",
                            confidence = "medium",
                            evidence = new[]
                            {
                                "2026-09-08: compressor not starting; start capacitor replaced, but still cutting in and out intermittently.",
                                "This report: clicking from the outdoor unit and repeated cut-outs."
                            }
                        },
                        new
                        {
                            cause = "Compressor tripping on its internal thermal overload",
                            confidence = "low",
                            evidence = new[]
                            {
                                "2026-09-08: unit left off pending parts after intermittent operation."
                            }
                        }
                    },
                    primary_hypothesis_index = 0,
                    recommended_next_action = "repair",
                    reasoning_summary = "The intermittent cut-out recorded on 2026-09-08 has come back although the start capacitor was replaced, which points at the switching side of the outdoor unit rather than the capacitor. The unit is still under warranty."
                },
                Proposal: new
                {
                    strategy = "single_job",
                    estimated_cost = 28_000.00m,
                    urgency = "high",
                    justification = "Replace the outdoor unit control board in one visit; the capacitor change did not stop the cut-outs. The unit is under warranty until 2026-11-18, so the manufacturer's agent should be asked to cover it first — this estimate is the cost if that claim is refused. Computer Lab 1 cannot be used in the afternoon without cooling.",
                    consolidate_with_work_order_ids = Array.Empty<int>()
                }),

            new LiveWorkOrderSeed(
                AssetTag: "PRJ-MAB102-01",
                ReportDescription: "Lecture Hall B projector picture has gone dim and yellowish, slides hard to read from the back rows.",
                Strategy: WorkOrderStrategy.KnownFix,
                EstimatedCost: 6_500m,
                PartsRequired: "Projector lamp, Epson ELPLP97.",
                Status: WorkOrderStatus.Approved,
                Diagnosis: null,
                Proposal: null)
        };

        var seeded = 0;

        foreach (var seed in seeds)
        {
            if (!assets.TryGetValue(seed.AssetTag, out var asset))
            {
                logger.LogWarning(
                    "Skipping seeded work order: asset {AssetTag} does not exist.", seed.AssetTag);
                continue;
            }

            // Guarded on the report's description, like the completed orders above.
            var description = seed.ReportDescription;
            if (await db.Reports.AnyAsync(r => r.Description == description, cancellationToken))
            {
                continue;
            }

            var report = new Report
            {
                ReporterId = reporterId.Value,
                RoomId = asset.RoomId,
                AssetId = asset.Id,
                Description = seed.ReportDescription,
                Status = ReportStatus.WorkOrderRaised
            };

            db.Reports.Add(report);

            db.WorkOrders.Add(new WorkOrder
            {
                Report = report,
                AssetId = asset.Id,
                Status = seed.Status,
                Strategy = seed.Strategy,
                EstimatedCost = seed.EstimatedCost,
                PartsRequired = seed.PartsRequired
            });

            // Saved per order: AgentWorkflow carries ReportId with no navigation to follow,
            // so the report needs its id before the workflow can point at it.
            await db.SaveChangesAsync(cancellationToken);

            if (seed.Diagnosis is not null && seed.Proposal is not null)
            {
                var workflow = new AgentWorkflow
                {
                    ReportId = report.Id,
                    // Verbatim, as ReportService raises it.
                    Objective = seed.ReportDescription,
                    CurrentState = WorkflowState.AwaitingManagerApproval,
                    StartedAt = DateTime.UtcNow
                };

                // Clarifier, diagnostic, strategist — the order and shape WorkflowRunner
                // writes. The report was clear enough that nothing needed asking.
                workflow.Steps.Add(SeededAgentStep("clarifier", new { questions = Array.Empty<object>() }, 7_800));
                workflow.Steps.Add(SeededAgentStep(AgentRunResponse.DiagnosticAgentName, seed.Diagnosis, 0));
                workflow.Steps.Add(SeededAgentStep(AgentRunResponse.StrategistAgentName, seed.Proposal, 0));

                db.AgentWorkflows.Add(workflow);
                await db.SaveChangesAsync(cancellationToken);
            }

            seeded++;
        }

        if (seeded == 0)
        {
            return;
        }

        logger.LogInformation("Seeded {Count} live work order(s) for the approval queue and dispatch board.", seeded);
    }

    private static AgentStep SeededAgentStep(string agentName, object output, int durationMs) => new()
    {
        AgentName = agentName,
        ToolCallsJson = "[]",
        PayloadJson = JsonSerializer.Serialize(output),
        DurationMs = durationMs,
        ValidationResult = "Ok"
    };

    /// <summary>
    /// One seeded live work order. <see cref="Diagnosis"/> and <see cref="Proposal"/> are the
    /// agent's output in its own snake_case shape, serialised verbatim into the steps; both
    /// null for an order raised without an agent run.
    /// </summary>
    private sealed record LiveWorkOrderSeed(
        string AssetTag,
        string ReportDescription,
        WorkOrderStrategy Strategy,
        decimal EstimatedCost,
        string PartsRequired,
        WorkOrderStatus Status,
        object? Diagnosis,
        object? Proposal);

    /// <summary>
    /// One seeded completed work order and the verification check raised against it.
    /// A record rather than a tuple purely for readability — there are twelve fields, and
    /// positional tuple elements stop being self-explanatory well before that.
    /// </summary>
    private sealed record VerificationSeed(
        string AssetTag,
        // Days before "now" that the work order was completed. DueAt is derived from this
        // plus the configured delay, so anything completed more recently than the delay is
        // legitimately not yet due.
        int DaysAgoCompleted,
        WorkOrderStrategy Strategy,
        decimal EstimatedCost,
        decimal ActualCost,
        bool Approved,
        string ReportDescription,
        string ResolutionNote,
        VerificationStatus CheckStatus,
        int? DaysAgoResponded,
        string? ReporterComment,
        string? AgentOutcome,
        string? AgentReason);

    private static async Task SeedUsersAsync(
        AppDbContext db,
        IConfiguration configuration,
        IPasswordHasher<User> passwordHasher,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Demo passwords come from configuration (user secrets or environment), never
        // from source. Missing key => that user is skipped, seeding is not blocked.
        var demoUsers = new[]
        {
            (Email: "reporter@campus.test", FullName: "Demo Reporter", Role: Role.Reporter, Key: "Seed:Passwords:Reporter"),
            (Email: "technician@campus.test", FullName: "Demo Technician", Role: Role.Technician, Key: "Seed:Passwords:Technician"),
            (Email: "manager@campus.test", FullName: "Demo Facilities Manager", Role: Role.FacilitiesManager, Key: "Seed:Passwords:FacilitiesManager"),
            (Email: "admin@campus.test", FullName: "Demo Admin", Role: Role.Admin, Key: "Seed:Passwords:Admin")
        };

        foreach (var demo in demoUsers)
        {
            var exists = await db.Users.AnyAsync(u => u.Email == demo.Email, cancellationToken);
            if (exists)
            {
                continue;
            }

            var password = configuration[demo.Key];
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning(
                    "Skipping demo user {Email}: no password configured at {Key}.",
                    demo.Email,
                    demo.Key);
                continue;
            }

            var user = new User
            {
                Email = demo.Email,
                FullName = demo.FullName,
                Role = demo.Role
            };
            user.PasswordHash = passwordHasher.HashPassword(user, password);

            db.Users.Add(user);
            logger.LogInformation("Seeding demo user {Email} with role {Role}.", demo.Email, demo.Role);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
