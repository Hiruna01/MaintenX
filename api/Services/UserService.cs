using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<UserDto>> GetByRoleAsync(Role role, CancellationToken cancellationToken = default)
    {
        // Id breaks ties, so two technicians with the same name keep one order in a picker.
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Role == role)
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.Id)
            .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.Role))
            .ToListAsync(cancellationToken);
    }
}
