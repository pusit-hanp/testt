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
using RoleValidation.Application.Employees;
using RoleValidation.Application.Roles;
using RoleValidation.Application.RoleOwners;
using RoleValidation.Application.SourceMappings;
using RoleValidation.Core.Features.Applications;
using RoleValidation.Core.Features.RoleValidation;
using RoleValidation.Core.Features.Roles;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Controllers;
using RoleValidation.Web.Models.RoleOwners;

namespace RoleValidation.Web.Tests.RoleOwners;

public sealed class RoleOwnersControllerTests
{
    [Fact]
    public async Task Index_Should_AllowAdminReadOnlyVisibilityWithoutSearchOrMutationState()
    {
        RoleOwnersController controller = CreateController(
            role: "Admin",
            owners:
            [
                new RoleOwnerRecord(
                    62,
                    17,
                    41,
                    "Warehouse Approver",
                    "00000001",
                    isActive: false),
                new RoleOwnerRecord(
                    61,
                    17,
                    41,
                    "Warehouse Approver",
                    "00234053",
                    isActive: true)
            ],
            employees:
            [
                CreateEmployee("00000001", "D"),
                CreateEmployee("00234053", "D")
            ]);

        IActionResult action = await controller.Index(17);

        ViewResult view = Assert.IsType<ViewResult>(action);
        RoleOwnerManagementViewModel model =
            Assert.IsType<RoleOwnerManagementViewModel>(view.Model);
        Assert.False(model.CanManage);
        Assert.Equal(2, model.Owners.Count);
        Assert.True(model.Owners[0].AssignmentIsActive);
        Assert.False(model.Owners[1].AssignmentIsActive);
        Assert.All(model.Owners, row => Assert.Equal(
            EmployeeStatusType.Inactive,
            row.EmployeeStatus));
        Assert.Empty(model.EmployeeSearchResults);
    }

    [Fact]
    public async Task Search_Should_ReturnMultipleCandidatesWithoutSelectingOne()
    {
        EmployeeSearchResult[] candidates =
        [
            new("00234053", "Jane Smith", "Operations", "Manager",
                EmployeeStatusType.Active),
            new("00234054", "Jane Stone", "Materials", "Buyer",
                EmployeeStatusType.Active)
        ];
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            searchResults: candidates);

        IActionResult action = await controller.Search(
            17,
            roleId: 41,
            roleOwnerId: null,
            query: " Jane ",
            selectedEmployeeNo: null,
            CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(action);
        RoleOwnerManagementViewModel model =
            Assert.IsType<RoleOwnerManagementViewModel>(view.Model);
        Assert.Equal(candidates, model.EmployeeSearchResults);
        Assert.Null(model.SelectedEmployeeNo);
        Assert.Equal("Jane", model.EmployeeSearchQuery);
        Assert.Equal(41, model.SearchRoleId);
    }

    [Fact]
    public async Task SearchModel_Should_MarkEmployeeAlreadyAssignedToSelectedRole()
    {
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            owners:
            [
                new RoleOwnerRecord(
                    61,
                    17,
                    41,
                    "Warehouse Approver",
                    "00234053",
                    isActive: true)
            ],
            employees: [CreateEmployee("00234053", "A")],
            searchResults:
            [
                new EmployeeSearchResult(
                    "00234053",
                    "Jane Smith",
                    "Operations",
                    "Manager",
                    EmployeeStatusType.Active)
            ]);

        ViewResult view = Assert.IsType<ViewResult>(await controller.Search(
            17,
            roleId: 41,
            roleOwnerId: null,
            query: "00234053",
            selectedEmployeeNo: null,
            CancellationToken.None));
        var model = Assert.IsType<RoleOwnerManagementViewModel>(view.Model);

        Assert.True(model.IsAlreadyAssigned(41, "00234053"));
        Assert.False(model.IsAlreadyAssigned(42, "00234053"));
    }

    [Fact]
    public async Task Search_Should_RejectBlankWithVisibleStableFeedback()
    {
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin");

        IActionResult action = await controller.Search(
            17,
            roleId: 41,
            roleOwnerId: null,
            query: "   ",
            selectedEmployeeNo: null,
            CancellationToken.None);

        ViewResult view = Assert.IsType<ViewResult>(action);
        RoleOwnerManagementViewModel model =
            Assert.IsType<RoleOwnerManagementViewModel>(view.Model);
        Assert.Contains("EMPLOYEE_SEARCH_REQUIRED", model.ErrorMessage);
        Assert.Empty(model.EmployeeSearchResults);
        Assert.Equal("owner-editor-new", model.DrawerErrorTarget);
        Assert.Equal("owner-error-new", model.FocusTarget);
    }

    [Theory]
    [InlineData(18, 41, null, "APPLICATION_NOT_FOUND")]
    [InlineData(17, null, 61, "OWNER_ASSIGNMENT_NOT_FOUND")]
    [InlineData(17, 9001, null, "ROLE_NOT_FOUND")]
    [InlineData(17, 42, null, "ROLE_INACTIVE")]
    public async Task SearchInvalidContext_Should_RedirectStableErrorToPageHeading(
        int applicationId,
        int? roleId,
        int? roleOwnerId,
        string errorCode)
    {
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin");

        IActionResult action = await controller.Search(
            applicationId,
            roleId,
            roleOwnerId,
            query: "Jane",
            selectedEmployeeNo: null,
            CancellationToken.None);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal(nameof(RoleOwnersController.Index), redirect.ActionName);
        Assert.Equal("page-heading", redirect.Fragment);
        Assert.Contains(
            errorCode,
            Assert.IsType<string>(controller.TempData["ManagementError"]));
        Assert.Equal("page-heading", controller.TempData["FocusTarget"]);
    }

    [Fact]
    public async Task FailedAssign_Should_PreserveRoleSearchAndSelectionForRetry()
    {
        var store = new RecordingAdministrationStore
        {
            AssignResult = new AdministrationResult(
                false,
                "OWNER_ASSIGNMENT_DUPLICATE",
                null)
        };
        EmployeeSearchResult[] candidates =
        [
            new("00234053", "Jane Smith", "Operations", "Manager",
                EmployeeStatusType.Active)
        ];
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            store: store,
            employees: [CreateEmployee("00234053", "A")],
            searchResults: candidates);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(
            await controller.Assign(
                17,
                41,
                "00234053",
                " Jane ",
                CancellationToken.None));

        Assert.Equal(nameof(RoleOwnersController.Search), redirect.ActionName);
        Assert.Equal("owner-editor-new", redirect.Fragment);
        Assert.Equal(17, redirect.RouteValues!["applicationId"]);
        Assert.Equal(41, redirect.RouteValues["roleId"]);
        Assert.Equal("Jane", redirect.RouteValues["query"]);
        Assert.Equal("00234053", redirect.RouteValues["selectedEmployeeNo"]);
        Assert.Equal("owner-error-new", controller.TempData["FocusTarget"]);

        ViewResult view = Assert.IsType<ViewResult>(await controller.Search(
            17,
            roleId: 41,
            roleOwnerId: null,
            query: "Jane",
            selectedEmployeeNo: "00234053",
            CancellationToken.None));
        var model = Assert.IsType<RoleOwnerManagementViewModel>(view.Model);

        Assert.Equal(41, model.SearchRoleId);
        Assert.Equal("Jane", model.EmployeeSearchQuery);
        Assert.Equal("00234053", model.SelectedEmployeeNo);
        Assert.Equal(candidates, model.EmployeeSearchResults);
        Assert.Equal("owner-editor-new", model.DrawerErrorTarget);
        Assert.Equal("owner-error-new", model.FocusTarget);
        Assert.Contains("OWNER_ASSIGNMENT_DUPLICATE", model.ErrorMessage);
    }

    [Fact]
    public async Task FailedReassign_Should_PreserveOwnerSearchAndSelectionForRetry()
    {
        var store = new RecordingAdministrationStore
        {
            ReassignResult = new AdministrationResult(
                false,
                "OWNER_ASSIGNMENT_DUPLICATE",
                null)
        };
        EmployeeSearchResult[] candidates =
        [
            new("00234054", "Jane Stone", "Materials", "Buyer",
                EmployeeStatusType.Active)
        ];
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            store: store,
            owners:
            [
                new RoleOwnerRecord(
                    61, 17, 41, "Warehouse Approver", "00234053", true)
            ],
            employees:
            [
                CreateEmployee("00234053", "A"),
                CreateEmployee("00234054", "A")
            ],
            searchResults: candidates);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(
            await controller.Reassign(
                17,
                61,
                "00234054",
                " Jane ",
                CancellationToken.None));

        Assert.Equal(nameof(RoleOwnersController.Search), redirect.ActionName);
        Assert.Equal("owner-editor-61", redirect.Fragment);
        Assert.Equal(61, redirect.RouteValues!["roleOwnerId"]);
        Assert.Equal("Jane", redirect.RouteValues["query"]);
        Assert.Equal("00234054", redirect.RouteValues["selectedEmployeeNo"]);
        Assert.Equal("owner-error-61", controller.TempData["FocusTarget"]);

        ViewResult view = Assert.IsType<ViewResult>(await controller.Search(
            17,
            roleId: null,
            roleOwnerId: 61,
            query: "Jane",
            selectedEmployeeNo: "00234054",
            CancellationToken.None));
        var model = Assert.IsType<RoleOwnerManagementViewModel>(view.Model);

        Assert.Equal(41, model.SearchRoleId);
        Assert.Equal(61, model.SearchRoleOwnerId);
        Assert.Equal("00234054", model.SelectedEmployeeNo);
        Assert.Equal(candidates, model.EmployeeSearchResults);
        Assert.Equal("owner-editor-61", model.DrawerErrorTarget);
        Assert.Equal("owner-error-61", model.FocusTarget);
    }

    [Fact]
    public async Task Admin_Should_ReadIndexButBeDeniedSearchAndEveryMutation()
    {
        using ServiceProvider services = BuildAuthorizationServices();
        IAuthorizationService authorization = services
            .GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal admin = CreatePrincipal("Admin");

        Assert.True((await authorization.AuthorizeAsync(
            admin,
            null,
            GetPolicy(nameof(RoleOwnersController.Index)))).Succeeded);
        foreach (string action in new[]
                 {
                     nameof(RoleOwnersController.Search),
                     nameof(RoleOwnersController.Assign),
                     nameof(RoleOwnersController.Reassign),
                     nameof(RoleOwnersController.Deactivate)
                 })
        {
            Assert.False((await authorization.AuthorizeAsync(
                admin,
                null,
                GetPolicy(action))).Succeeded);
        }
    }

    [Fact]
    public void Search_Should_RequireLocalItAndRemainReadOnlyGet()
    {
        MethodInfo action = typeof(RoleOwnersController)
            .GetMethod(nameof(RoleOwnersController.Search))!;

        Assert.Equal(
            RoleValidationAuthorizationPolicies.LocalItAdministration,
            action.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(action.GetCustomAttribute<HttpPostAttribute>());
    }

    [Theory]
    [InlineData(nameof(RoleOwnersController.Assign))]
    [InlineData(nameof(RoleOwnersController.Reassign))]
    [InlineData(nameof(RoleOwnersController.Deactivate))]
    public void Mutation_Should_RequirePostAntiforgeryAndLocalItAdministration(
        string actionName)
    {
        string allowedActionName = actionName switch
        {
            nameof(RoleOwnersController.Assign) => nameof(RoleOwnersController.Assign),
            nameof(RoleOwnersController.Reassign) => nameof(RoleOwnersController.Reassign),
            nameof(RoleOwnersController.Deactivate) => nameof(RoleOwnersController.Deactivate),
            _ => throw new ArgumentOutOfRangeException(nameof(actionName))
        };
        MethodInfo action = typeof(RoleOwnersController).GetMethod(allowedActionName)!;

        Assert.Equal(
            RoleValidationAuthorizationPolicies.LocalItAdministration,
            action.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(
            action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public async Task Assign_Should_TrimServerValuesAndRestoreFocusToReplacementRow()
    {
        var store = new RecordingAdministrationStore
        {
            AssignResult = new AdministrationResult(true, null, 71)
        };
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            store: store,
            employees: [CreateEmployee("00234053", "A")],
            employeeNo: " C1008267 ");

        IActionResult action = await controller.Assign(
            17,
            41,
            " 00234053 ",
            query: null,
            CancellationToken.None);

        Assert.Equal(
            new AssignRoleOwnerCommand(17, 41, "00234053", "C1008267"),
            store.AssignCommand);
        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal("owner-71", redirect.Fragment);
        Assert.Equal("owner-71", controller.TempData["FocusTarget"]);
        Assert.Equal("Role Owner assigned.",
            controller.TempData["ManagementSuccess"]);
    }

    [Fact]
    public async Task Assign_Should_SurfaceRequiredEmployeeWithoutCallingStore()
    {
        var store = new RecordingAdministrationStore();
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            store: store);

        IActionResult action = await controller.Assign(
            17,
            41,
            "  ",
            "Jane",
            CancellationToken.None);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal("owner-editor-new", redirect.Fragment);
        Assert.Contains("OWNER_EMPLOYEE_REQUIRED",
            Assert.IsType<string>(controller.TempData["ManagementError"]));
        Assert.Null(store.AssignCommand);
    }

    [Fact]
    public async Task Reassign_Should_FailClosedForInactiveEmployeeAndReopenOwnerDrawer()
    {
        var store = new RecordingAdministrationStore();
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            store: store,
            employees: [CreateEmployee("00234053", "D")]);

        IActionResult action = await controller.Reassign(
            17,
            61,
            "00234053",
            "Jane",
            CancellationToken.None);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(action);
        Assert.Equal("owner-editor-61", redirect.Fragment);
        Assert.Contains("OWNER_EMPLOYEE_INACTIVE",
            Assert.IsType<string>(controller.TempData["ManagementError"]));
        Assert.Null(store.ReassignCommand);
    }

    [Fact]
    public async Task AssignMissingRole_Should_AlignFocusTargetAndRedirectFragment()
    {
        var store = new RecordingAdministrationStore
        {
            AssignResult = new AdministrationResult(
                false,
                "ROLE_NOT_FOUND",
                null)
        };
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            store: store,
            employees: [CreateEmployee("00234053", "A")]);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(await controller.Assign(
                17,
                9001,
                "00234053",
                "Jane",
                CancellationToken.None));

        Assert.Equal("page-heading", redirect.Fragment);
        Assert.Equal("page-heading", controller.TempData["FocusTarget"]);
    }

    [Fact]
    public async Task ReassignMissingAssignment_Should_AlignFocusTargetAndRedirectFragment()
    {
        var store = new RecordingAdministrationStore
        {
            ReassignResult = new AdministrationResult(
                false,
                "OWNER_ASSIGNMENT_NOT_FOUND",
                null)
        };
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            store: store,
            employees: [CreateEmployee("00234053", "A")]);

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(await controller.Reassign(
                17,
                9001,
                "00234053",
                "Jane",
                CancellationToken.None));

        Assert.Equal("page-heading", redirect.Fragment);
        Assert.Equal("page-heading", controller.TempData["FocusTarget"]);
    }

    [Fact]
    public async Task DeactivateSuccess_Should_FocusSameNowInactiveHistoryRow()
    {
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin");

        RedirectToActionResult redirect =
            Assert.IsType<RedirectToActionResult>(await controller.Deactivate(
                17,
                61,
                CancellationToken.None));

        Assert.Equal("owner-61", redirect.Fragment);
        Assert.Equal("owner-61", controller.TempData["FocusTarget"]);
    }

    [Theory]
    [InlineData(0, 41, "APPLICATION_NOT_FOUND")]
    [InlineData(17, 0, "ROLE_NOT_FOUND")]
    public async Task AssignNonPositiveIds_Should_ReturnStablePrgWithoutMutation(
        int applicationId,
        int roleId,
        string errorCode)
    {
        var store = new RecordingAdministrationStore();
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            store: store,
            employees: [CreateEmployee("00234053", "A")]);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(
            await controller.Assign(
                applicationId,
                roleId,
                "00234053",
                "Jane",
                CancellationToken.None));

        Assert.Equal(nameof(RoleOwnersController.Index), redirect.ActionName);
        Assert.Equal("page-heading", redirect.Fragment);
        Assert.Contains(errorCode,
            Assert.IsType<string>(controller.TempData["ManagementError"]));
        Assert.Null(store.AssignCommand);
    }

    [Theory]
    [InlineData(0, 61, "APPLICATION_NOT_FOUND")]
    [InlineData(17, 0, "OWNER_ASSIGNMENT_NOT_FOUND")]
    public async Task ReassignNonPositiveIds_Should_ReturnStablePrgWithoutMutation(
        int applicationId,
        int roleOwnerId,
        string errorCode)
    {
        var store = new RecordingAdministrationStore();
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            store: store,
            employees: [CreateEmployee("00234054", "A")]);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(
            await controller.Reassign(
                applicationId,
                roleOwnerId,
                "00234054",
                "Jane",
                CancellationToken.None));

        Assert.Equal(nameof(RoleOwnersController.Index), redirect.ActionName);
        Assert.Equal("page-heading", redirect.Fragment);
        Assert.Contains(errorCode,
            Assert.IsType<string>(controller.TempData["ManagementError"]));
        Assert.Null(store.ReassignCommand);
    }

    [Theory]
    [InlineData(0, 61, "APPLICATION_NOT_FOUND")]
    [InlineData(17, 0, "OWNER_ASSIGNMENT_NOT_FOUND")]
    public async Task DeactivateNonPositiveIds_Should_ReturnStablePrgWithoutMutation(
        int applicationId,
        int roleOwnerId,
        string errorCode)
    {
        var store = new RecordingAdministrationStore();
        RoleOwnersController controller = CreateController(
            role: "Local_IT_Admin",
            store: store);

        RedirectToActionResult redirect = Assert.IsType<RedirectToActionResult>(
            await controller.Deactivate(
                applicationId,
                roleOwnerId,
                CancellationToken.None));

        Assert.Equal(nameof(RoleOwnersController.Index), redirect.ActionName);
        Assert.Equal("page-heading", redirect.Fragment);
        Assert.Contains(errorCode,
            Assert.IsType<string>(controller.TempData["ManagementError"]));
        Assert.Null(store.DeactivateCommand);
    }

    private static RoleOwnersController CreateController(
        string role,
        RecordingAdministrationStore? store = null,
        IReadOnlyList<RoleOwnerRecord>? owners = null,
        IReadOnlyList<EmployeeRecord>? employees = null,
        IReadOnlyList<EmployeeSearchResult>? searchResults = null,
        string employeeNo = "C1008267")
    {
        store ??= new RecordingAdministrationStore();
        var roleReader = new StubRoleReader(
        [
            ValidationRole.Restore(41, 17, "Warehouse Approver", true, true),
            ValidationRole.Restore(42, 17, "Dormant", false, false)
        ]);
        var ownerReader = new StubRoleOwnerReader(owners ?? []);
        var employeeReader = new StubEmployeeReader(employees ?? []);
        var searchReader = new StubEmployeeSearchReader(searchResults ?? []);
        var controller = new RoleOwnersController(
            new ApplicationAdministrationHandler(store),
            new ValidationRoleAdministrationHandler(store, roleReader),
            new RoleOwnerAdministrationHandler(
                store,
                ownerReader,
                employeeReader,
                searchReader));
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
        typeof(RoleOwnersController)
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

    private static EmployeeRecord CreateEmployee(
        string employeeNo,
        string status)
    {
        return new EmployeeRecord(
            employeeNo,
            "Jane Smith",
            new EmployeeStatus(status),
            email: "not-for-owner-ui@example.com",
            position: "Manager",
            department: "Operations");
    }

    private sealed class StubRoleReader(IReadOnlyList<ValidationRole> roles)
        : IValidationRoleReader
    {
        public Task<IReadOnlyList<ValidationRole>> GetByApplicationAsync(
            int applicationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationRole>>(
                roles.Where(role => role.ApplicationId == applicationId).ToList());

        public Task<ValidationRole?> FindByIdAsync(
            int roleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(roles.FirstOrDefault(role => role.RoleId == roleId));
    }

    private sealed class StubRoleOwnerReader(
        IReadOnlyList<RoleOwnerRecord> owners) : IRoleOwnerReader
    {
        public Task<IReadOnlyList<RoleOwnerRecord>> GetByApplicationAsync(
            int applicationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoleOwnerRecord>>(
                owners.Where(owner => owner.ApplicationId == applicationId).ToList());
    }

    private sealed class StubEmployeeReader(
        IReadOnlyList<EmployeeRecord> employees) : IEmployeeReader
    {
        public Task<IReadOnlyList<EmployeeRecord>> FindByEmployeeNosAsync(
            IReadOnlyCollection<string> employeeNos,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmployeeRecord>>(
                employees.Where(employee => employeeNos.Contains(
                    employee.EmployeeNo,
                    StringComparer.OrdinalIgnoreCase)).ToList());

        public Task<IReadOnlyList<EmployeeRecord>> FindByUserNamesAsync(
            IReadOnlyCollection<string> userNames,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubEmployeeSearchReader(
        IReadOnlyList<EmployeeSearchResult> results) : IEmployeeSearchReader
    {
        public Task<IReadOnlyList<EmployeeSearchResult>> SearchAsync(
            string query,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(results);
    }

    private sealed class RecordingAdministrationStore
        : IRoleValidationAdministrationStore
    {
        public AdministrationResult AssignResult { get; init; } =
            new(true, null, 71);

        public AdministrationResult ReassignResult { get; init; } =
            new(true, null, 72);

        public AdministrationResult DeactivateResult { get; init; } =
            new(true, null, 61);

        public AssignRoleOwnerCommand? AssignCommand { get; private set; }

        public ReassignRoleOwnerCommand? ReassignCommand { get; private set; }

        public DeactivateRoleOwnerCommand? DeactivateCommand { get; private set; }

        public Task<IReadOnlyList<ApplicationAdministrationRow>>
            GetApplicationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationAdministrationRow>>(
            [
                new(
                    17,
                    "WAREHOUSE",
                    "Warehouse",
                    true,
                    new ApplicationDependencyCounts(2, 1, 0))
            ]);

        public Task<AdministrationResult> AssignOwnerAsync(
            AssignRoleOwnerCommand command,
            CancellationToken cancellationToken = default)
        {
            AssignCommand = command;
            return Task.FromResult(AssignResult);
        }

        public Task<AdministrationResult> ReassignOwnerAsync(
            ReassignRoleOwnerCommand command,
            CancellationToken cancellationToken = default)
        {
            ReassignCommand = command;
            return Task.FromResult(ReassignResult);
        }

        public Task<AdministrationResult> DeactivateOwnerAsync(
            DeactivateRoleOwnerCommand command,
            CancellationToken cancellationToken = default)
        {
            DeactivateCommand = command;
            return Task.FromResult(DeactivateResult);
        }

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
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdministrationResult> DeactivateValidationRoleAsync(
            DeactivateValidationRoleCommand command,
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
