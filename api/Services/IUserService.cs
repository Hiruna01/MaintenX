using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace CampusFacilities.Api.Services;

public interface IUserService
{
    /// <summary>
    /// Every user holding <paramref name="role"/>, by name. What a manager picks a technician
    /// from when assigning or filtering work orders.
    ///
    /// Always by ONE role: there is deliberately no way to list the whole user table, since
    /// nothing in the application needs one.
    /// </summary>
    Task<IReadOnlyList<UserDto>> GetByRoleAsync(Role role, CancellationToken cancellationToken = default);
}
