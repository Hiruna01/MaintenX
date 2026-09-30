using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampusFacilities.Api.Dtos;
using CampusFacilities.Api.Models;

namespace api.Tests;

/// <summary>
/// The Admin's user management: every action Admin-only, "delete" a deactivation that keeps
/// the history, a deactivated or re-roled account refused on its very next request, and the
/// two refusals that keep the system usable — an Admin cannot remove themselves, and a
/// Technician with live work cannot be switched off out from under it.
/// </summary>
public class UserManagementTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const string Password = "UserPass123";

    private readonly ApiFactory _factory;

    public UserManagementTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------------------------------
    // Access
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task WithoutAToken_EveryUserEndpointIs401()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/users", NewUser(Role.Reporter), JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync("/api/users/1", new UpdateUserDto("a@campus.test", "A", Role.Reporter), JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/api/users/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/users/1/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/users/1/password", new ResetPasswordDto("NewPass123"), JsonOptions)).StatusCode);
    }

    [Theory]
    [InlineData(Role.Reporter)]
    [InlineData(Role.Technician)]
    [InlineData(Role.FacilitiesManager)]
    public async Task ANonAdmin_Is403OnEveryManagementAction(Role role)
    {
        var (client, ownId) = await ClientAsync(role);
        var (_, otherId) = await ClientAsync(Role.Reporter);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/users/{ownId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/users", NewUser(Role.Admin), JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"/api/users/{ownId}", new UpdateUserDto(UniqueEmail(), "Me", Role.Admin), JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/users/{otherId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/users/{otherId}/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/users/{otherId}/password", new ResetPasswordDto("NewPass123"), JsonOptions)).StatusCode);
    }

    // ---------------------------------------------------------------------------------
    // Create and read
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_Is201_AtItsOwnUrl_AndTheAccountCanSignIn_WithItsRole()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var email = UniqueEmail();

        var response = await admin.PostAsJsonAsync(
            "/api/users", new CreateUserDto($"  {email.ToUpperInvariant()} ", "New Technician", Password, Role.Technician), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<UserAdminDto>(JsonOptions);
        Assert.Equal(email, created!.Email);
        Assert.Equal(Role.Technician, created.Role);
        Assert.True(created.IsActive);
        Assert.Equal(0, created.LiveWorkOrderCount);
        Assert.Equal($"/api/users/{created.Id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);

        var login = await LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(Role.Technician, (await login.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!.Role);
    }

    [Fact]
    public async Task NoResponse_EverCarriesAPasswordHash()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var created = await CreateAsync(admin, Role.Reporter);

        var single = await admin.GetStringAsync($"/api/users/{created.Id}");
        var page = await admin.GetStringAsync("/api/users?pageSize=100");

        Assert.DoesNotContain("password", single, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WithAnEmailAlreadyInUse_Is409_WhateverItsCase()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var created = await CreateAsync(admin, Role.Reporter);

        var duplicate = await admin.PostAsJsonAsync(
            "/api/users", new CreateUserDto(created.Email.ToUpperInvariant(), "Twin", Password, Role.Reporter), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutARole_Is400_NotAReporterChosenForTheAdmin()
    {
        var admin = await _factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/users", new { email = UniqueEmail(), fullName = "No Role", password = Password }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAShortPassword_Is400()
    {
        var admin = await _factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/users", new CreateUserDto(UniqueEmail(), "Short", "short", Role.Reporter), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ForAnUnknownUser_Is404()
    {
        var admin = await _factory.CreateAdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/users/999999")).StatusCode);
    }

    [Fact]
    public async Task List_SearchesNameOrEmail_AndFiltersRoleAndActive_CountingBeforePaging()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];

        var byName = await CreateAsync(admin, Role.Technician, fullName: $"Tech {marker}");
        var byEmail = await CreateAsync(admin, Role.Reporter, email: $"{marker}@campus.test");
        var inactive = await CreateAsync(admin, Role.Technician, fullName: $"Gone {marker}");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{inactive.Id}")).StatusCode);

        var all = await ListAsync(admin, $"search={marker.ToUpperInvariant()}");
        Assert.Equal(3, all.TotalCount);

        var technicians = await ListAsync(admin, $"search={marker}&role=Technician");
        Assert.Equal(new[] { inactive.Id, byName.Id }.OrderBy(i => i), technicians.Items.Select(u => u.Id).OrderBy(i => i));

        var activeOnly = await ListAsync(admin, $"search={marker}&isActive=true");
        Assert.Equal(new[] { byEmail.Id, byName.Id }.OrderBy(i => i), activeOnly.Items.Select(u => u.Id).OrderBy(i => i));

        var inactiveOnly = await ListAsync(admin, $"search={marker}&isActive=false");
        Assert.Equal(inactive.Id, Assert.Single(inactiveOnly.Items).Id);

        var firstPage = await ListAsync(admin, $"search={marker}&pageSize=2");
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
    }

    [Fact]
    public async Task List_WithAnUnknownRole_Is400_RatherThanAnEmptyPage()
    {
        var admin = await _factory.CreateAdminClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/users?role=Wizard")).StatusCode);
    }

    // ---------------------------------------------------------------------------------
    // Update
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesNameEmailAndRole_AndIs204()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var created = await CreateAsync(admin, Role.Reporter);
        var newEmail = UniqueEmail();

        var response = await admin.PutAsJsonAsync(
            $"/api/users/{created.Id}", new UpdateUserDto(newEmail, "Promoted Person", Role.FacilitiesManager), JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var read = await admin.GetFromJsonAsync<UserAdminDto>($"/api/users/{created.Id}", JsonOptions);
        Assert.Equal(newEmail, read!.Email);
        Assert.Equal("Promoted Person", read.FullName);
        Assert.Equal(Role.FacilitiesManager, read.Role);
    }

    [Fact]
    public async Task Update_ToAnEmailAnotherAccountHas_Is409_AndChangesNothing()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var first = await CreateAsync(admin, Role.Reporter);
        var second = await CreateAsync(admin, Role.Reporter);

        var response = await admin.PutAsJsonAsync(
            $"/api/users/{second.Id}", new UpdateUserDto(first.Email, "Renamed", Role.Reporter), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var read = await admin.GetFromJsonAsync<UserAdminDto>($"/api/users/{second.Id}", JsonOptions);
        Assert.Equal(second.Email, read!.Email);
        Assert.Equal(second.FullName, read.FullName);
    }

    [Fact]
    public async Task Update_AnUnknownUser_Is404()
    {
        var admin = await _factory.CreateAdminClientAsync();

        var response = await admin.PutAsJsonAsync(
            "/api/users/999999", new UpdateUserDto(UniqueEmail(), "Nobody", Role.Reporter), JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------
    // Deactivate / reactivate — and the token that stops working
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task Deactivate_KeepsTheRowAndItsHistory_AndRefusesTheLogin_AsAWrongPasswordWould()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (reporter, reporterId, email) = await SignedInAsync(Role.Reporter);
        var room = await NewRoomAsync();

        // History that points at this user with a Restrict foreign key: a real delete would fail.
        var filed = await reporter.PostAsJsonAsync(
            "/api/reports", new CreateReportDto("The projector keeps switching itself off.", room), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, filed.StatusCode);
        var report = await filed.Content.ReadFromJsonAsync<ReportDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{reporterId}")).StatusCode);

        var read = await admin.GetFromJsonAsync<UserAdminDto>($"/api/users/{reporterId}", JsonOptions);
        Assert.False(read!.IsActive);
        var stillThere = await admin.GetFromJsonAsync<ReportDetailDto>($"/api/reports/{report!.Id}", JsonOptions);
        Assert.Equal(reporterId, stillThere!.ReporterId);

        var login = await LoginAsync(email, Password);
        var wrongPassword = await LoginAsync(email, "NotThePassword1");
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, login.StatusCode);
        // Same answer, word for word (each carries its own traceId, so compare the words).
        var refused = await login.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var wrong = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(wrong.GetProperty("title").GetString(), refused.GetProperty("title").GetString());
        Assert.Equal(
            wrong.TryGetProperty("detail", out var d1) ? d1.GetString() : null,
            refused.TryGetProperty("detail", out var d2) ? d2.GetString() : null);
    }

    [Fact]
    public async Task ADeactivatedUsersToken_Is401_OnItsVeryNextRequest()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (reporter, reporterId, _) = await SignedInAsync(Role.Reporter);

        Assert.Equal(HttpStatusCode.OK, (await reporter.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{reporterId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await reporter.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ARoleChange_RetiresTheOldToken_AndTheNextLoginCarriesTheNewRole()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (reporter, reporterId, email) = await SignedInAsync(Role.Reporter);

        var update = await admin.PutAsJsonAsync(
            $"/api/users/{reporterId}", new UpdateUserDto(email, "Now A Technician", Role.Technician), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        // The old token still SAYS Reporter; the account no longer is one.
        Assert.Equal(HttpStatusCode.Unauthorized, (await reporter.GetAsync("/api/auth/me")).StatusCode);

        var login = await LoginAsync(email, Password);
        Assert.Equal(Role.Technician, (await login.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!.Role);
    }

    [Fact]
    public async Task ANameChange_LeavesTheSessionAlone()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (reporter, reporterId, email) = await SignedInAsync(Role.Reporter);

        await admin.PutAsJsonAsync(
            $"/api/users/{reporterId}", new UpdateUserDto(email, "Renamed Reporter", Role.Reporter), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, (await reporter.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Reactivate_LetsTheAccountSignInAgain_AndBothDirectionsAreIdempotent()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (_, userId, email) = await SignedInAsync(Role.Reporter);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, Password)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{userId}/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{userId}/reactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, Password)).StatusCode);
    }

    [Fact]
    public async Task DeactivateAndReactivate_AnUnknownUser_Is404()
    {
        var admin = await _factory.CreateAdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/users/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/users/999999/reactivate", null)).StatusCode);
    }

    // ---------------------------------------------------------------------------------
    // The two refusals
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task AnAdmin_CannotDeactivateThemselves_NorChangeTheirOwnRole_ButMayRenameThemselves()
    {
        var (admin, adminId, email) = await SignedInAsync(Role.Admin);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/users/{adminId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(
            $"/api/users/{adminId}", new UpdateUserDto(email, "Test User", Role.Reporter), JsonOptions)).StatusCode);

        // Nothing was written: still an active Admin, still signed in.
        var self = await admin.GetFromJsonAsync<UserAdminDto>($"/api/users/{adminId}", JsonOptions);
        Assert.True(self!.IsActive);
        Assert.Equal(Role.Admin, self.Role);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(
            $"/api/users/{adminId}", new UpdateUserDto(email, "Renamed Admin", Role.Admin), JsonOptions)).StatusCode);
    }

    [Fact]
    public async Task AnotherAdmin_MayBeDeactivated()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (_, otherAdminId, _) = await SignedInAsync(Role.Admin);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{otherAdminId}")).StatusCode);
    }

    [Fact]
    public async Task ATechnicianWithLiveWork_CannotBeDeactivatedOrReRoled_AndCannotBeAssignedOnceInactive()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (manager, _, _) = await SignedInAsync(Role.FacilitiesManager);
        var (_, technicianId, email) = await SignedInAsync(Role.Technician);
        var orderId = await AssignedOrderAsync(manager, technicianId);

        var busy = await admin.GetFromJsonAsync<UserAdminDto>($"/api/users/{technicianId}", JsonOptions);
        Assert.Equal(1, busy!.LiveWorkOrderCount);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/users/{technicianId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(
            $"/api/users/{technicianId}", new UpdateUserDto(email, "Test User", Role.Reporter), JsonOptions)).StatusCode);

        // A rename is not taking them off the job.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(
            $"/api/users/{technicianId}", new UpdateUserDto(email, "Renamed Technician", Role.Technician), JsonOptions)).StatusCode);

        // Reassign the job, and the technician is free to go.
        var (_, otherTechnicianId, _) = await SignedInAsync(Role.Technician);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(
            $"/api/workorders/{orderId}/assign", new AssignTechnicianDto(otherTechnicianId), JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{technicianId}")).StatusCode);

        // And an inactive technician cannot be handed it back.
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(
            $"/api/workorders/{orderId}/assign", new AssignTechnicianDto(technicianId), JsonOptions)).StatusCode);
    }

    // ---------------------------------------------------------------------------------
    // Reset password
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task ResetPassword_ReplacesThePassword()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (_, userId, email) = await SignedInAsync(Role.Technician);

        var reset = await admin.PostAsJsonAsync($"/api/users/{userId}/password", new ResetPasswordDto("Temporary123"), JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, "Temporary123")).StatusCode);
    }

    [Fact]
    public async Task ResetPassword_IsHeldToTheSameRules_AndIs404ForAnUnknownUser()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var (_, userId, _) = await SignedInAsync(Role.Reporter);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"/api/users/{userId}/password", new ResetPasswordDto("short"), JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync("/api/users/999999/password", new ResetPasswordDto("Temporary123"), JsonOptions)).StatusCode);
    }

    // ---------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@campus.test";

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static CreateUserDto NewUser(Role role) => new(UniqueEmail(), "Test User", Password, role);

    private async Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), JsonOptions);

    private static async Task<UserAdminDto> CreateAsync(
        HttpClient admin,
        Role role,
        string? fullName = null,
        string? email = null)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/users", new CreateUserDto(email ?? UniqueEmail(), fullName ?? "Test User", Password, role), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserAdminDto>(JsonOptions))!;
    }

    private static async Task<PagedResult<UserAdminDto>> ListAsync(HttpClient admin, string query) =>
        (await admin.GetFromJsonAsync<PagedResult<UserAdminDto>>($"/api/users?{query}", JsonOptions))!;

    private async Task<(HttpClient Client, int UserId)> ClientAsync(Role role)
    {
        var (client, userId, _) = await SignedInAsync(role);
        return (client, userId);
    }

    /// <summary>An account registered through the real endpoint, and a client signed in as it.</summary>
    private async Task<(HttpClient Client, int UserId, string Email)> SignedInAsync(Role role)
    {
        var email = UniqueEmail();
        var response = await _factory.RegisterAsync(new RegisterRequest(email, Password, "Test User", role));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        return (client, auth.UserId, email);
    }

    private async Task<int> NewRoomAsync()
    {
        var estate = await _factory.CreateAdminClientAsync();

        var building = await (await estate.PostAsJsonAsync(
                "/api/buildings", new CreateBuildingDto("Main Block", UniqueCode()), JsonOptions))
            .Content.ReadFromJsonAsync<BuildingDto>(JsonOptions);

        var room = await (await estate.PostAsJsonAsync(
                "/api/rooms", new CreateRoomDto(building!.Id, "Lecture Hall", UniqueCode(), 1), JsonOptions))
            .Content.ReadFromJsonAsync<RoomDto>(JsonOptions);

        return room!.Id;
    }

    /// <summary>
    /// A room, an asset, a report, and an auto-approved order on it assigned to
    /// <paramref name="technicianId"/> — live work only that technician can complete.
    /// </summary>
    private async Task<int> AssignedOrderAsync(HttpClient manager, int technicianId)
    {
        var admin = await _factory.CreateAdminClientAsync();
        var roomId = await NewRoomAsync();

        var category = await (await admin.PostAsJsonAsync(
                "/api/assetcategories", new CreateAssetCategoryDto($"Projectors {UniqueCode()}", 24), JsonOptions))
            .Content.ReadFromJsonAsync<AssetCategoryDto>(JsonOptions);

        var asset = await (await admin.PostAsJsonAsync(
                "/api/assets",
                new CreateAssetDto(UniqueCode(), "Ceiling Projector", category!.Id, roomId, "Acme", "X1",
                    new DateOnly(2024, 1, 15), null),
                JsonOptions))
            .Content.ReadFromJsonAsync<AssetDto>(JsonOptions);

        var (reporter, _, _) = await SignedInAsync(Role.Reporter);
        var report = await (await reporter.PostAsJsonAsync(
                "/api/reports", new CreateReportDto("Projector keeps cutting out mid-lecture.", roomId), JsonOptions))
            .Content.ReadFromJsonAsync<ReportDto>(JsonOptions);

        await WorkflowTestData.ReadyForWorkOrderAsync(_factory.Services, report!.Id);

        var raised = await manager.PostAsJsonAsync(
            "/api/workorders",
            new CreateWorkOrderDto(report.Id, asset!.Id, WorkOrderStrategy.SingleJob, 500m, null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, raised.StatusCode);
        var order = await raised.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions);

        var assigned = await manager.PutAsJsonAsync(
            $"/api/workorders/{order!.Id}/assign", new AssignTechnicianDto(technicianId), JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);

        return order.Id;
    }
}
