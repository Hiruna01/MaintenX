using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CampusFacilities.Api.Data;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CampusFacilities.Api.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly JwtSettings _jwtSettings;

    public AuthService(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        JwtSettings jwtSettings)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtSettings = jwtSettings;
    }

    public async Task<AuthResponse?> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = NormaliseEmail(request.Email);

        if (await _db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return null;
        }

        var user = new User
        {
            Email = email,
            FullName = request.FullName,
            Role = request.Role
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two requests for the same address can both pass the AnyAsync check above and
            // race to the insert. The unique index on Users.Email is the real guard; turning
            // that race into a 409 here is what stops it surfacing as an unhandled 500.
            _db.Entry(user).State = EntityState.Detached;

            if (await IsDuplicateEmailAsync(email, cancellationToken))
            {
                return null;
            }

            // Some other database failure — let the exception middleware answer with a 500.
            throw;
        }

        return new AuthResponse(
            CreateToken(user),
            user.Id,
            user.Email,
            user.FullName,
            user.Role);
    }

    public async Task<AuthResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = NormaliseEmail(request.Email);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The stored hash used older parameters. The password was correct, so upgrade it.
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new AuthResponse(
            CreateToken(user),
            user.Id,
            user.Email,
            user.FullName,
            user.Role);
    }

    public async Task<UserDto?> GetByIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user is null
            ? null
            : new UserDto(user.Id, user.Email, user.FullName, user.Role);
    }

    /// <summary>Emails are compared case-insensitively by storing them lower-cased,
    /// so Admin@campus.test and admin@campus.test cannot both be registered.</summary>
    private static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();

    private Task<bool> IsDuplicateEmailAsync(string email, CancellationToken cancellationToken) =>
        _db.Users.AsNoTracking().AnyAsync(u => u.Email == email, cancellationToken);

    private string CreateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Exactly three claims: sub, email, role. Role is written as the enum NAME so the
        // token reads "FacilitiesManager", matching how the database stores it.
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("role", user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(_jwtSettings.TokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
