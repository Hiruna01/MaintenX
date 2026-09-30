using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CampusFacilities.Api.Services;

public class UserService : IUserService
{
    private const int MaxPageSize = 100;

    /// <summary>
    /// The statuses an order is finished in. An assigned order in any OTHER status is live
    /// work only its Technician can complete, which is what blocks deactivating them or
    /// moving them to another role.
    /// </summary>
    private static readonly WorkOrderStatus[] FinishedStatuses =
    {
        WorkOrderStatus.Completed,
        WorkOrderStatus.Rejected,
        WorkOrderStatus.Cancelled
    };

    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;

    public UserService(AppDbContext db, IPasswordHasher<User> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task<IReadOnlyList<UserDto>> GetActiveTechniciansAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Role == Role.Technician && u.IsActive)
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.Id)
            .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.Role))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<UserAdminDto>> GetAllAsync(
        string? search = null,
        Role? role = null,
        bool? isActive = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Clamp rather than reject, the same as every other list.
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : Math.Min(pageSize, MaxPageSize);

        var query = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ToLower().Contains(), never EF.Functions.ILike — the same reason as the asset
            // search: ILike is Npgsql-only and the tests run on SQLite.
            var term = search.Trim().ToLower();

            query = query.Where(u =>
                u.FullName.ToLower().Contains(term) || u.Email.ToLower().Contains(term));
        }

        if (role is not null)
        {
            query = query.Where(u => u.Role == role);
        }

        if (isActive is not null)
        {
            query = query.Where(u => u.IsActive == isActive);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Id breaks every tie — two people can share a name, and a paged list needs a total order.
        var items = await Project(query.OrderBy(u => u.FullName).ThenBy(u => u.Id))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<UserAdminDto>(items, page, pageSize, totalCount);
    }

    public Task<UserAdminDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Project(_db.Users.AsNoTracking().Where(u => u.Id == id))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<UserAdminDto?> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default)
    {
        var email = AuthService.NormaliseEmail(dto.Email);

        // Checked first so a duplicate is a 409 naming the problem; the unique index on Email
        // is still the real guard, and a lost race on it is re-checked in TrySaveAsync.
        if (await EmailTakenAsync(email, exceptId: null, cancellationToken))
        {
            return null;
        }

        var user = new User
        {
            Email = email,
            FullName = dto.FullName.Trim(),
            Role = dto.Role!.Value
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);

        _db.Users.Add(user);

        if (!await TrySaveAsync(email, exceptId: null, cancellationToken))
        {
            return null;
        }

        return await GetByIdAsync(user.Id, cancellationToken);
    }

    public async Task<UserWriteOutcome> UpdateAsync(
        int id,
        int callerId,
        UpdateUserDto dto,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return UserWriteOutcome.NotFound;
        }

        var newRole = dto.Role!.Value;

        // An Admin editing their own name or email is fine; changing their own role is how
        // the last Admin would demote themselves out of the system.
        if (id == callerId && newRole != user.Role)
        {
            return UserWriteOutcome.OwnAccount;
        }

        if (user.Role == Role.Technician && newRole != Role.Technician
            && await HasLiveWorkAsync(id, cancellationToken))
        {
            return UserWriteOutcome.HasLiveWork;
        }

        var email = AuthService.NormaliseEmail(dto.Email);

        if (await EmailTakenAsync(email, exceptId: id, cancellationToken))
        {
            return UserWriteOutcome.EmailTaken;
        }

        user.Email = email;
        user.FullName = dto.FullName.Trim();
        user.Role = newRole;

        return await TrySaveAsync(email, exceptId: id, cancellationToken)
            ? UserWriteOutcome.Success
            : UserWriteOutcome.EmailTaken;
    }

    public async Task<UserWriteOutcome> DeactivateAsync(
        int id,
        int callerId,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return UserWriteOutcome.NotFound;
        }

        if (id == callerId)
        {
            return UserWriteOutcome.OwnAccount;
        }

        // Already off is still what the caller asked for — the same as retiring an asset
        // that is already retired.
        if (!user.IsActive)
        {
            return UserWriteOutcome.Success;
        }

        if (user.Role == Role.Technician && await HasLiveWorkAsync(id, cancellationToken))
        {
            return UserWriteOutcome.HasLiveWork;
        }

        user.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);
        return UserWriteOutcome.Success;
    }

    public async Task<UserWriteOutcome> ReactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return UserWriteOutcome.NotFound;
        }

        if (!user.IsActive)
        {
            user.IsActive = true;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return UserWriteOutcome.Success;
    }

    public async Task<UserWriteOutcome> ResetPasswordAsync(
        int id,
        ResetPasswordDto dto,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return UserWriteOutcome.NotFound;
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);
        await _db.SaveChangesAsync(cancellationToken);
        return UserWriteOutcome.Success;
    }

    /// <summary>
    /// The one projection to <see cref="UserAdminDto"/>, so the list and the detail cannot
    /// disagree. The live order count is a correlated subquery, translated on both providers.
    /// </summary>
    private IQueryable<UserAdminDto> Project(IQueryable<User> users) =>
        users.Select(u => new UserAdminDto(
            u.Id,
            u.Email,
            u.FullName,
            u.Role,
            u.IsActive,
            _db.WorkOrders.Count(w => w.AssignedTechnicianId == u.Id && !FinishedStatuses.Contains(w.Status)),
            u.CreatedAt,
            u.UpdatedAt));

    private Task<bool> HasLiveWorkAsync(int userId, CancellationToken cancellationToken) =>
        _db.WorkOrders.AnyAsync(
            w => w.AssignedTechnicianId == userId && !FinishedStatuses.Contains(w.Status),
            cancellationToken);

    private Task<bool> EmailTakenAsync(string email, int? exceptId, CancellationToken cancellationToken) =>
        _db.Users.AnyAsync(u => u.Email == email && u.Id != exceptId, cancellationToken);

    /// <summary>
    /// Saves, turning a lost race on the unique Email index into false instead of a 500. Any
    /// other database failure is rethrown for the exception middleware.
    /// </summary>
    private async Task<bool> TrySaveAsync(string email, int? exceptId, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Detached first, so the re-check reads the database and not the failed write.
            _db.ChangeTracker.Clear();

            if (await EmailTakenAsync(email, exceptId, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }
}
