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
