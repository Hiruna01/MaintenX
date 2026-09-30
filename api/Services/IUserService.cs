using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

public interface IUserService
{
    /// <summary>
    /// Every ACTIVE Technician, by name — the picker behind assigning a work order and the
    /// board's technician filter. A deactivated technician cannot be handed a job, so they
    /// are not offered.
    /// </summary>
    Task<IReadOnlyList<UserDto>> GetActiveTechniciansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of accounts for the Admin's user management page. <paramref name="search"/>
    /// matches the name or the email; <paramref name="role"/> and <paramref name="isActive"/>
    /// are exact filters, and all three combine. Ordered by name with an Id tiebreak.
    /// </summary>
    Task<PagedResult<UserAdminDto>> GetAllAsync(
        string? search = null,
        Role? role = null,
        bool? isActive = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<UserAdminDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the email already has an account — a 409.</summary>
    Task<UserAdminDto?> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edits the name, email and role. <paramref name="callerId"/> is the Admin making the
    /// change: an Admin may not change their OWN role (<see cref="UserWriteOutcome.OwnAccount"/>).
    /// </summary>
    Task<UserWriteOutcome> UpdateAsync(
        int id,
        int callerId,
        UpdateUserDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What DELETE /api/users/{id} does: the account is switched off, never removed — every
    /// report, answer and work order that names this person keeps pointing at a real row.
    /// Already inactive is still Success. An Admin may not deactivate themselves.
    /// </summary>
    Task<UserWriteOutcome> DeactivateAsync(int id, int callerId, CancellationToken cancellationToken = default);

    /// <summary>Switches an account back on. Already active is still Success.</summary>
    Task<UserWriteOutcome> ReactivateAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Sets a new password. Only NotFound can go wrong.</summary>
    Task<UserWriteOutcome> ResetPasswordAsync(
        int id,
        ResetPasswordDto dto,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What a write to an account did, so the controller can pick the status code instead of a
/// constraint violation surfacing as a 500 — the same shape as <see cref="EstateWriteOutcome"/>.
/// </summary>
public enum UserWriteOutcome
{
    /// <summary>Written. 204.</summary>
    Success,

    /// <summary>No such user. 404.</summary>
    NotFound,

    /// <summary>Another account already has that email. 409.</summary>
    EmailTaken,

    /// <summary>
    /// An Admin tried to deactivate themselves or change their own role. 409.
    ///
    /// This one rule is what guarantees there is always an active Admin: only an active
    /// Admin can reach these endpoints (the token check refuses anyone else), and no Admin
    /// can remove themselves, so the caller is always still there afterwards. It also stops
    /// an Admin locking themselves out in the middle of their own session.
    /// </summary>
    OwnAccount,

    /// <summary>
    /// A Technician still has unfinished work orders assigned, so they cannot be deactivated
    /// or given another role — only the assigned Technician can complete an order, so those
    /// orders would be stuck. Reassign them first. 409.
    /// </summary>
    HasLiveWork
}
