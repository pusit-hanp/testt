using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RoleValidation.Application.Administration;
using RoleValidation.Application.Authorization;
using RoleValidation.Application.Roles;
using RoleValidation.Application.RoleOwners;
using RoleValidation.Application.SourceMappings;
using RoleValidation.Core.Features.Applications;
using RoleValidation.Core.Features.Roles;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Controllers;
using RoleValidation.Web.Models.ValidationRoles;

namespace RoleValidation.Web.Tests.ValidationRoles;

public sealed class ValidationRolesControllerTests
{
    [Fact]
    public async Task Index_Should_LoadSelectedApplicationAndActiveThenInactiveRoles()
    {
        var store = new RecordingAdministrationStore
        {
            Applications =
            [
                new(
                    17,
                    "WAREHOUSE",
                    "Warehouse",
                    true,
                    new ApplicationDependencyCounts(2, 0, 0))
            ]
        };
        var reader = new StubRoleReader(
        [
            ValidationRole.Restore(42, 17, "Dormant", false, false),
            ValidationRole.Restore(41, 17, "Auditor", true, true)
        ]);
        ValidationRolesController controller = CreateController(
            store,
            reader,
            "Admin");

        IActionResult action = await controller.Index(
            17,
            CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(action);
        ValidationRoleManagementViewModel model =
            Assert.IsType<ValidationRoleManagementViewModel>(view.Model);
        Assert.Equal("Warehouse", model.SelectedApplicationName);
        Assert.False(model.CanManage);
        Assert.Equal([41, 42], model.Roles.Select(role => role.RoleId));
        Assert.Equal((2, 1, 1, 1),
            (model.TotalCount,
             model.ActiveCount,
             model.AuditedCount,
             model.InactiveCount));
    }

    [Fact]
    public async Task Index_Should_ClearInvalidApplicationContextAndNotLoadRoles()
    {
        var store = new RecordingAdministrationStore
        {
            Applications =
            [
                new(
                    17,
                    "WAREHOUSE",
                    "Warehouse",
                    true,
                    new ApplicationDependencyCounts(0, 0, 0))
            ]
        };
        var reader = new StubRoleReader([]);
        ValidationRolesController controller = CreateController(
            store,
            reader,
            "Local_IT_Admin");

        IActionResult action = await controller.Index(
            999,
            CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(action);
        ValidationRoleManagementViewModel model =
            Assert.IsType<ValidationRoleManagementViewModel>(view.Model);
        Assert.Null(model.SelectedApplicationId);
        Assert.Null(model.SelectedApplicationName);
        Assert.Equal(
            "APPLICATION_NOT_FOUND: The selected Application was not found.",
            model.ErrorMessage);
        Assert.Equal(0, reader.GetByApplicationCallCount);
    }

    [Fact]
    public async Task Admin_Should_BeAllowedToReadButDeniedToRoleMutations()
    {
        using ServiceProvider services = BuildAuthorizationServices();
        IAuthorizationService authorization = services
            .GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal admin = CreatePrincipal("Admin");

        Assert.True((await authorization.AuthorizeAsync(
            admin,
            null,
            GetPolicy(nameof(ValidationRolesController.Index)))).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            admin,
            null,
            GetPolicy(nameof(ValidationRolesController.Save)))).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            admin,
            null,
            GetPolicy(nameof(ValidationRolesController.Deactivate)))).Succeeded);
    }

    [Theory]
    [InlineData(nameof(ValidationRolesController.Save))]
    [InlineData(nameof(ValidationRolesController.Deactivate))]
    public void Mutation_Should_RequirePostAntiforgeryAndLocalItAdministration(
        string actionName)
    {
        string allowedActionName = actionName switch
        {
            nameof(ValidationRolesController.Save) => nameof(ValidationRolesController.Save),
            nameof(ValidationRolesController.Deactivate) => nameof(ValidationRolesController.Deactivate),
            _ => throw new ArgumentOutOfRangeException(nameof(actionName))
        };
        MethodInfo action = typeof(ValidationRolesController)
            .GetMethod(allowedActionName)!;

        Assert.Equal(
            RoleValidationAuthorizationPolicies.LocalItAdministration,
            action.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(
            action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public async Task Save_Should_UsePrgAndRequestExplicitReactivationForInactiveCollision()
    {
        var store = new RecordingAdministrationStore
        {
            SaveResult = new AdministrationResult(
                false,
                "ROLE_REACTIVATION_REQUIRED",
                42)
        };
        ValidationRolesController controller = CreateController(
            store,
            new StubRoleReader([]),
            "Local_IT_Admin",
            employeeNo: " C1008267 ");

        IActionResult action = await controller.Save(
            17,
            roleId: null,
            roleName: "  Dormant  ",
            isAudited: true,
            reactivate: false,
            CancellationToken.None);

        Assert.Equal(
            new SaveValidationRoleCommand(
                17,
                null,
                "Dormant",
                true,
                false,
                "C1008267"),
            store.SaveCommand);
        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal(nameof(ValidationRolesController.Index), redirect.ActionName);
        Assert.Equal(17, redirect.RouteValues!["applicationId"]);
        Assert.Equal("role-reactivation", redirect.Fragment);
        Assert.Equal(42, controller.TempData["ReactivationRoleId"]);
        Assert.Equal("Dormant", controller.TempData["EditorRoleName"]);
    }

    [Fact]
    public async Task Save_Should_ReopenEditedRoleAndPreserveSubmittedValuesOnFailure()
    {
        var store = new RecordingAdministrationStore
        {
            SaveResult = new AdministrationResult(
                false,
                "ROLE_NAME_DUPLICATE",
                null)
        };
        ValidationRolesController controller = CreateController(
            store,
            new StubRoleReader([]),
            "Local_IT_Admin");

        IActionResult action = await controller.Save(
            17,
            roleId: 41,
            roleName: "  Submitted role  ",
            isAudited: true,
            reactivate: false,
            CancellationToken.None);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal("role-editor-41", redirect.Fragment);
        Assert.Equal(41, controller.TempData["EditorRoleId"]);
        Assert.Equal("Submitted role", controller.TempData["EditorRoleName"]);
        Assert.Equal(true, controller.TempData["EditorIsAudited"]);
    }

    [Fact]
    public async Task Save_Should_KeepAddFailureInNewRoleEditor()
    {
        var store = new RecordingAdministrationStore
        {
            SaveResult = new AdministrationResult(
                false,
                "ROLE_NAME_DUPLICATE",
                null)
        };
        ValidationRolesController controller = CreateController(
            store,
            new StubRoleReader([]),
            "Local_IT_Admin");

        IActionResult action = await controller.Save(
            17,
            roleId: null,
            roleName: "  New duplicate  ",
            isAudited: false,
            reactivate: false,
            CancellationToken.None);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal("role-editor-new", redirect.Fragment);
        Assert.Null(controller.TempData["EditorRoleId"]);
        Assert.Equal("New duplicate", controller.TempData["EditorRoleName"]);
    }

    [Fact]
    public async Task Save_Should_BindConfirmedReactivationToExactRoleId()
    {
        var store = new RecordingAdministrationStore
        {
            SaveResult = new AdministrationResult(true, null, 42)
        };
        ValidationRolesController controller = CreateController(
            store,
            new StubRoleReader([]),
            "Local_IT_Admin");

        IActionResult action = await controller.Save(
            17,
            roleId: 42,
            roleName: " Dormant ",
            isAudited: false,
            reactivate: true,
            CancellationToken.None);

        Assert.Equal(
            new SaveValidationRoleCommand(
                17,
                42,
                "Dormant",
                false,
                true,
                "C1008267"),
            store.SaveCommand);
        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal("role-42", redirect.Fragment);
    }

    [Fact]
    public async Task Save_Should_SurfaceStableRoleNotFoundForCrossApplicationEdit()
    {
        var store = new RecordingAdministrationStore
        {
            SaveResult = new AdministrationResult(
                false,
                "ROLE_NOT_FOUND",
                null)
        };
        ValidationRolesController controller = CreateController(
            store,
            new StubRoleReader([]),
            "Local_IT_Admin");

        IActionResult action = await controller.Save(
            17,
            roleId: 9001,
            roleName: "Other Application role",
            isAudited: false,
            reactivate: false,
            CancellationToken.None);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal("page-heading", redirect.Fragment);
        Assert.Equal(
            "ROLE_NOT_FOUND: The Validation Role was not found in the selected Application.",
            controller.TempData["ManagementError"]);
    }

    [Fact]
    public async Task Deactivate_Should_SurfaceRoleDependencyErrorWithoutGuessingCounts()
    {
        var store = new RecordingAdministrationStore
        {
            DeactivateResult = new AdministrationResult(
                false,
                "ROLE_HAS_ACTIVE_DEPENDENCIES",
                41)
        };
        ValidationRolesController controller = CreateController(
            store,
            new StubRoleReader([]),
            "Local_IT_Admin");

        IActionResult action = await controller.Deactivate(
            17,
            41,
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(action);
        string error = Assert.IsType<string>(
            controller.TempData["ManagementError"]);
        Assert.Contains("ROLE_HAS_ACTIVE_DEPENDENCIES", error);
        Assert.Contains("active Owner assignment or Source Mapping", error);
        Assert.DoesNotContain("0", error);
    }

    [Fact]
    public async Task Deactivate_Should_SurfaceStableRoleNotFoundWithoutThrowing()
    {
        var store = new RecordingAdministrationStore
        {
            DeactivateResult = new AdministrationResult(
                false,
                "ROLE_NOT_FOUND",
                null)
        };
        ValidationRolesController controller = CreateController(
            store,
            new StubRoleReader([]),
            "Local_IT_Admin");

        IActionResult action = await controller.Deactivate(
            17,
            9001,
            CancellationToken.None);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal("page-heading", redirect.Fragment);
        Assert.Equal(
            "ROLE_NOT_FOUND: The Validation Role was not found in the selected Application.",
            controller.TempData["ManagementError"]);
    }

    private static ValidationRolesController CreateController(
        RecordingAdministrationStore store,
        IValidationRoleReader reader,
        string role,
        string employeeNo = "C1008267")
    {
        var controller = new ValidationRolesController(
            new ApplicationAdministrationHandler(store),
            new ValidationRoleAdministrationHandler(store, reader));
        var httpContext = new DefaultHttpContext
        {
            User = CreatePrincipal(role, employeeNo)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new TempDataDictionary(
            httpContext,
            Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static string GetPolicy(string actionName) =>
        typeof(ValidationRolesController)
            .GetMethod(actionName)!
            .GetCustomAttribute<AuthorizeAttribute>()!
            .Policy!;

    private static ServiceProvider BuildAuthorizationServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoleValidationAuthorization();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreatePrincipal(
        string role,
        string employeeNo = "C1008267")
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "person.user"),
                new Claim(ClaimTypes.Role, role),
                new Claim(
                    RoleValidationAuthenticationDefaults.EmployeeNoClaimType,
                    employeeNo)
            ],
            RoleValidationAuthenticationDefaults.CookieScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));
    }

    private sealed class StubRoleReader(IReadOnlyList<ValidationRole> roles)
        : IValidationRoleReader
    {
        public int GetByApplicationCallCount { get; private set; }

        public Task<IReadOnlyList<ValidationRole>> GetByApplicationAsync(
            int applicationId,
            CancellationToken cancellationToken = default)
        {
            GetByApplicationCallCount++;
            return Task.FromResult(roles);
        }

        public Task<ValidationRole?> FindByIdAsync(
            int roleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(roles.FirstOrDefault(role => role.RoleId == roleId));
    }

    private sealed class RecordingAdministrationStore
        : IRoleValidationAdministrationStore
    {
        public IReadOnlyList<ApplicationAdministrationRow> Applications
            { get; init; } = [];

        public AdministrationResult SaveResult { get; init; } =
            new(true, null, 41);

        public AdministrationResult DeactivateResult { get; init; } =
            new(true, null, 41);

        public SaveValidationRoleCommand? SaveCommand { get; private set; }

        public Task<IReadOnlyList<ApplicationAdministrationRow>>
            GetApplicationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Applications);

        public Task<AdministrationResult> RenameApplicationAsync(
            RenameApplicationCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> SetApplicationActiveAsync(
            SetApplicationActiveCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> SaveValidationRoleAsync(
            SaveValidationRoleCommand command,
            CancellationToken cancellationToken = default)
        {
            SaveCommand = command;
            return Task.FromResult(SaveResult);
        }

        public Task<AdministrationResult> DeactivateValidationRoleAsync(
            DeactivateValidationRoleCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DeactivateResult);

        public Task<AdministrationResult> AssignOwnerAsync(
            AssignRoleOwnerCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> ReassignOwnerAsync(
            ReassignRoleOwnerCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> DeactivateOwnerAsync(
            DeactivateRoleOwnerCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> AddSourceMappingAsync(
            AddSourceMappingCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> ReplaceSourceMappingAsync(
            ReplaceSourceMappingCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> DeactivateSourceMappingAsync(
            DeactivateSourceMappingCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuthorizedUserAdministrationRow>>
            GetAuthorizedUsersAsync(
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> AddAuthorizedUserAsync(
            AddAuthorizedUserCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> ChangeAuthorizedUserAsync(
            ChangeAuthorizedUserCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> DeactivateAuthorizedUserAsync(
            DeactivateAuthorizedUserCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> ReactivateAuthorizedUserAsync(
            ReactivateAuthorizedUserCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
