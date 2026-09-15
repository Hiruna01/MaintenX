using CampusFacilities.Api.Models;
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
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        await SeedBuildingsAndRoomsAsync(db, cancellationToken);
        await SeedAssetRegistryAsync(db, cancellationToken);
        await SeedUsersAsync(db, configuration, passwordHasher, logger, cancellationToken);
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
