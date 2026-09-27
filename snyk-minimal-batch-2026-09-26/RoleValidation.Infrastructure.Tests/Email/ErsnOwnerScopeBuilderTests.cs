using RoleValidation.Application.Applications;
using RoleValidation.Application.Email;
using RoleValidation.Application.Employees;
using RoleValidation.Application.RoleOwners;
using RoleValidation.Application.Roles;
using RoleValidation.Application.RoleValidation;
using RoleValidation.Core.Features.RoleValidation;
using RoleValidation.Core.Features.Roles;
using RoleValidation.Core.Features.SourceMappings;
using RoleValidation.Infrastructure.Email;

namespace RoleValidation.Infrastructure.Tests.Email;

public sealed class ErsnOwnerScopeBuilderTests
{
    private static readonly string TestRunId = Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData("ERSN")]
    [InlineData("PULL_LIST")]
    [InlineData("IDM")]
    [InlineData("MFM")]
    [InlineData("EVA")]
    [InlineData("ICPLB")]
    [InlineData("OPM")]
    public async Task BuildAsync_Should_AcceptEachApprovedApplication(
        string applicationCode)
    {
        var roleReader = new StubValidationRoleReader();
        var ownerReader = new StubRoleOwnerReader();
        var employeeReader = new StubEmployeeReader();
        var loader = new RecordingApplicationUserLoader([]);
        var builder = new ErsnOwnerScopeBuilder(
            new StubApplicationReader(
                new ApplicationSummary(17, applicationCode, applicationCode)),
            roleReader,
            ownerReader,
            employeeReader,
            loader.LoadAsync);

        OwnerScopeBuildResult result = await builder.BuildAsync(17);

        Assert.Equal(applicationCode, result.ApplicationCode);
        Assert.Empty(result.Scopes);
        Assert.Equal(1, roleReader.CallCount);
        Assert.Equal(1, ownerReader.CallCount);
        Assert.Equal(0, employeeReader.EmployeeNoCallCount);
        Assert.Empty(loader.Requests);
    }

    [Fact]
    public async Task BuildAsync_Should_GroupOwnersAndKeepOnlyTheirActiveAuditedApplicationRoles()
    {
        var applicationReader = new StubApplicationReader(
            new ApplicationSummary(17, " eRsN ", "eRSN"));
        var roleReader = new StubValidationRoleReader(
            Role(10, 17, audited: true, active: true),
            Role(11, 17, audited: true, active: true),
            Role(12, 17, audited: false, active: true),
            Role(13, 17, audited: true, active: false),
            Role(14, 18, audited: true, active: true));
        var ownerReader = new StubRoleOwnerReader(
            Owner(1, 17, 10, "C1000001", active: true),
            Owner(2, 17, 11, " c1000001 ", active: true),
            Owner(3, 17, 10, "C1000002", active: true),
            Owner(4, 17, 12, "C1000001", active: true),
            Owner(5, 17, 13, "C1000001", active: true),
            Owner(6, 17, 10, "C1000003", active: false),
            Owner(7, 18, 14, "C1000004", active: true));
        var employeeReader = new StubEmployeeReader(
            Employee("C1000001", "A"),
            Employee("C1000002", "A"),
            Employee("C1000003", "A"),
            Employee("C1000004", "A"));
        ApplicationUserView role10 = User("user-10", 10, audited: true);
        ApplicationUserView role11 = User("user-11", 11, audited: true);
        var loader = new RecordingApplicationUserLoader(
        [
            role10,
            role11,
            User("non-audited-role", 12, audited: false),
            User("inactive-role", 13, audited: true),
            User("defensive-audit", 10, audited: false)
        ]);
        var builder = new ErsnOwnerScopeBuilder(
            applicationReader,
            roleReader,
            ownerReader,
            employeeReader,
            loader.LoadAsync);

        OwnerScopeBuildResult result = await builder.BuildAsync(17);

        Assert.Equal(17, result.ApplicationId);
        Assert.Equal("eRsN", result.ApplicationCode);
        Assert.Equal("eRSN", result.ApplicationName);
        Assert.Equal(2, result.Scopes.Count);
        Assert.All(result.DeliverySeeds, seed => Assert.Equal(
            OwnerDeliverySeedOutcome.Pending,
            seed.Outcome));

        OwnerScope first = Assert.Single(result.Scopes, scope =>
            scope.OwnerEmployeeNo == "C1000001");
        Assert.Equal([10, 11], first.ActiveAuditedRoleIds);
        Assert.Equal([role10, role11], first.Rows);

        OwnerScope second = Assert.Single(result.Scopes, scope =>
            scope.OwnerEmployeeNo == "C1000002");
        Assert.Equal([10], second.ActiveAuditedRoleIds);
        Assert.Equal([role10], second.Rows);

        LoadApplicationUserRequest request = Assert.Single(loader.Requests);
        Assert.Equal(17, request.ApplicationId);
        Assert.Equal(MappedRoleSelectionMode.All, request.MappedRoleSelectionMode);
        Assert.Empty(request.MappedRoleIds);
        Assert.Equal(FilterSelectionMode.Selected, request.AuditedSelectionMode);
        Assert.Equal([true], request.AuditedStatuses);
        Assert.Equal(FilterSelectionMode.All, request.ResolutionSelectionMode);
        Assert.Empty(request.ResolutionTypes);
        Assert.Equal(FilterSelectionMode.All, request.EmployeeStatusSelectionMode);
        Assert.True(request.IncludeAll);

        Assert.Equal(1, employeeReader.EmployeeNoCallCount);
        Assert.Equal(
            ["C1000001", "C1000002"],
            employeeReader.LastEmployeeNos.Order(
                StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_Should_CreateExactSeedsForEveryEmployeeResolutionIncludingEmptyWorkbook()
    {
        var employeeReader = new StubEmployeeReader(
            Employee("C1000001", "A"),
            Employee("C1000002", "D"),
            Employee("C1000004", "A", "First duplicate"),
            Employee("c1000004", "A", "Second duplicate"),
            Employee("C1000005", "X"));
        var loader = new RecordingApplicationUserLoader([]);
        var builder = new ErsnOwnerScopeBuilder(
            new StubApplicationReader(
                new ApplicationSummary(17, "ERSN", "eRSN")),
            new StubValidationRoleReader(
                Role(10, 17, audited: true, active: true)),
            new StubRoleOwnerReader(
                Owner(1, 17, 10, "C1000001", active: true),
                Owner(2, 17, 10, "C1000002", active: true),
                Owner(3, 17, 10, "C1000003", active: true),
                Owner(4, 17, 10, "C1000004", active: true),
                Owner(5, 17, 10, "C1000005", active: true)),
            employeeReader,
            loader.LoadAsync);

        OwnerScopeBuildResult result = await builder.BuildAsync(17);

        Assert.Equal(5, result.Scopes.Count);
        AssertSeed(result, "C1000001", OwnerDeliverySeedOutcome.Pending, null);
        AssertSeed(result, "C1000002", OwnerDeliverySeedOutcome.Failed, "OWNER_INACTIVE");
        AssertSeed(result, "C1000003", OwnerDeliverySeedOutcome.Failed, "OWNER_EMPLOYEE_NOT_FOUND");
        AssertSeed(result, "C1000004", OwnerDeliverySeedOutcome.Failed, "OWNER_EMPLOYEE_AMBIGUOUS");
        AssertSeed(result, "C1000005", OwnerDeliverySeedOutcome.Failed, "OWNER_EMPLOYEE_STATUS_UNKNOWN");
        Assert.Empty(result.Scopes.Single(scope =>
            scope.OwnerEmployeeNo == "C1000001").Rows);
        Assert.Equal(1, employeeReader.EmployeeNoCallCount);
        Assert.Equal(5, employeeReader.LastEmployeeNos.Count);
        Assert.Single(loader.Requests);
    }

    [Fact]
    public async Task BuildAsync_Should_RejectUnknownApplicationBeforeReadingScopeDependencies()
    {
        var roleReader = new StubValidationRoleReader();
        var ownerReader = new StubRoleOwnerReader();
        var employeeReader = new StubEmployeeReader();
        var loader = new RecordingApplicationUserLoader([]);
        var builder = new ErsnOwnerScopeBuilder(
            new StubApplicationReader(
                new ApplicationSummary(17, " FUTURE_APP ", "Future App")),
            roleReader,
            ownerReader,
            employeeReader,
            loader.LoadAsync);

        InvalidOperationException error = await Assert.ThrowsAsync<
            InvalidOperationException>(() => builder.BuildAsync(17));

        Assert.Equal("EMAIL_WORKBOOK_CONTRACT_NOT_APPROVED", error.Message);
        Assert.Equal(0, roleReader.CallCount);
        Assert.Equal(0, ownerReader.CallCount);
        Assert.Equal(0, employeeReader.EmployeeNoCallCount);
        Assert.Empty(loader.Requests);
    }

    [Fact]
    public async Task BuildAsync_Should_FailClosedWhenAnyActiveAuditedRoleHasNoActiveOwner()
    {
        var roleReader = new StubValidationRoleReader(
            Role(10, 17, audited: true, active: true),
            Role(11, 17, audited: true, active: true));
        var ownerReader = new StubRoleOwnerReader(
            Owner(1, 17, 10, "C1000001", active: true));
        var employeeReader = new StubEmployeeReader(
            Employee("C1000001", "A"));
        var loader = new RecordingApplicationUserLoader(
            [User("unexpected", 10, audited: true)]);
        var builder = new ErsnOwnerScopeBuilder(
            new StubApplicationReader(
                new ApplicationSummary(17, "ERSN", "eRSN")),
            roleReader,
            ownerReader,
            employeeReader,
            loader.LoadAsync);

        InvalidOperationException error = await Assert.ThrowsAsync<
            InvalidOperationException>(() => builder.BuildAsync(17));

        Assert.Equal("ROLE_OWNER_COVERAGE_INCOMPLETE", error.Message);
        Assert.Equal(1, roleReader.CallCount);
        Assert.Equal(1, ownerReader.CallCount);
        Assert.Equal(0, employeeReader.EmployeeNoCallCount);
        Assert.Empty(loader.Requests);
    }

    [Theory]
    [InlineData(SourceRoleResolutionType.NotMapped)]
    [InlineData(SourceRoleResolutionType.Ambiguous)]
    [InlineData(SourceRoleResolutionType.MappingTargetMissing)]
    public async Task BuildAsync_Should_FailClosedWhenAnnualSelectionContainsUnresolvedMapping(
        SourceRoleResolutionType resolutionType)
    {
        var loader = new RequestFilteringApplicationUserLoader(
        [
            User("resolved", 10, audited: true),
            UnresolvedUser("unresolved", resolutionType)
        ]);
        var builder = new ErsnOwnerScopeBuilder(
            new StubApplicationReader(
                new ApplicationSummary(17, "ERSN", "eRSN")),
            new StubValidationRoleReader(
                Role(10, 17, audited: true, active: true)),
            new StubRoleOwnerReader(
                Owner(1, 17, 10, "C1000001", active: true)),
            new StubEmployeeReader(
                Employee("C1000001", "A")),
            loader.LoadAsync);

        InvalidOperationException error = await Assert.ThrowsAsync<
            InvalidOperationException>(() => builder.BuildAsync(17));

        Assert.Equal("SOURCE_ROLE_MAPPING_INCOMPLETE", error.Message);
        LoadApplicationUserRequest request = Assert.Single(loader.Requests);
        Assert.Equal(MappedRoleSelectionMode.All, request.MappedRoleSelectionMode);
        Assert.Empty(request.MappedRoleIds);
        Assert.Equal(FilterSelectionMode.Selected, request.AuditedSelectionMode);
        Assert.Equal([true], request.AuditedStatuses);
        Assert.Equal(FilterSelectionMode.All, request.ResolutionSelectionMode);
        Assert.Empty(request.ResolutionTypes);
        Assert.True(request.IncludeAll);
    }

    [Fact]
    public async Task BuildAsync_Should_RejectMissingApplicationBeforeReadingScopeDependencies()
    {
        var roleReader = new StubValidationRoleReader();
        var ownerReader = new StubRoleOwnerReader();
        var employeeReader = new StubEmployeeReader();
        var loader = new RecordingApplicationUserLoader([]);
        var builder = new ErsnOwnerScopeBuilder(
            new StubApplicationReader(application: null),
            roleReader,
            ownerReader,
            employeeReader,
            loader.LoadAsync);

        InvalidOperationException error = await Assert.ThrowsAsync<
            InvalidOperationException>(() => builder.BuildAsync(17));

        Assert.Equal("APPLICATION_NOT_FOUND", error.Message);
        Assert.Equal(0, roleReader.CallCount);
        Assert.Equal(0, ownerReader.CallCount);
        Assert.Equal(0, employeeReader.EmployeeNoCallCount);
        Assert.Empty(loader.Requests);
    }

    [Fact]
    public async Task BuildAsync_Should_ReturnNoSeedsWhenNoActiveAuditedRoleExists()
    {
        var employeeReader = new StubEmployeeReader(
            Employee("C1000001", "A"));
        var loader = new RecordingApplicationUserLoader(
            [User("unexpected", 10, true)]);
        var builder = new ErsnOwnerScopeBuilder(
            new StubApplicationReader(
                new ApplicationSummary(17, "ERSN", "eRSN")),
            new StubValidationRoleReader(
                Role(10, 17, audited: false, active: true),
                Role(11, 17, audited: true, active: false)),
            new StubRoleOwnerReader(
                Owner(1, 17, 10, "C1000001", active: true),
                Owner(2, 17, 11, "C1000001", active: true)),
            employeeReader,
            loader.LoadAsync);

        OwnerScopeBuildResult result = await builder.BuildAsync(17);

        Assert.Empty(result.Scopes);
        Assert.Empty(result.DeliverySeeds);
        Assert.Equal(0, employeeReader.EmployeeNoCallCount);
        Assert.Empty(loader.Requests);
    }

    [Fact]
    public async Task BuildAsync_Should_PropagateDependencyFailureWithoutLoadingUsers()
    {
        var expected = new InvalidOperationException("role owner read failed");
        var loader = new RecordingApplicationUserLoader([]);
        var builder = new ErsnOwnerScopeBuilder(
            new StubApplicationReader(
                new ApplicationSummary(17, "ERSN", "eRSN")),
            new StubValidationRoleReader(
                Role(10, 17, audited: true, active: true)),
            new StubRoleOwnerReader(expected),
            new StubEmployeeReader(),
            loader.LoadAsync);

        InvalidOperationException actual = await Assert.ThrowsAsync<
            InvalidOperationException>(() => builder.BuildAsync(17));

        Assert.Same(expected, actual);
        Assert.Empty(loader.Requests);
    }

    private static void AssertSeed(
        OwnerScopeBuildResult result,
        string employeeNo,
        OwnerDeliverySeedOutcome expectedOutcome,
        string? expectedErrorCode)
    {
        OwnerDeliverySeed seed = result.Scopes.Single(scope =>
            scope.OwnerEmployeeNo == employeeNo).DeliverySeed;
        Assert.Equal(expectedOutcome, seed.Outcome);
        Assert.Equal(expectedErrorCode, seed.ErrorCode);
    }

    private static ValidationRole Role(
        int roleId,
        int applicationId,
        bool audited,
        bool active) =>
        ValidationRole.Restore(
            roleId,
            applicationId,
            $"Role {roleId}",
            audited,
            active);

    private static RoleOwnerRecord Owner(
        int roleOwnerId,
        int applicationId,
        int roleId,
        string employeeNo,
        bool active) =>
        new(
            roleOwnerId,
            applicationId,
            roleId,
            $"Role {roleId}",
            employeeNo,
            active);

    private static EmployeeRecord Employee(
        string employeeNo,
        string status,
        string? name = null) =>
        new(
            employeeNo,
            name ?? employeeNo,
            new EmployeeStatus(status));

    private static ApplicationUserView User(
        string label,
        int roleId,
        bool audited,
        SourceRoleResolutionType resolutionType =
            SourceRoleResolutionType.Resolved) =>
        new(
            employeeNo: $"E{roleId:D7}",
            employeeName: label,
            employeeStatus: new EmployeeStatus("A"),
            sourceRoleKey: $"SOURCE_{label}",
            sourceRoleDisplayName: label,
            roleId,
            roleName: $"Role {roleId}",
            resolutionType,
            userName: $"{label}.{TestRunId}",
            isAudited: audited);

    private static ApplicationUserView UnresolvedUser(
        string label,
        SourceRoleResolutionType resolutionType) =>
        new(
            employeeNo: "E9999999",
            employeeName: label,
            employeeStatus: new EmployeeStatus("A"),
            sourceRoleKey: $"SOURCE_{label}",
            sourceRoleDisplayName: label,
            roleId: null,
            roleName: null,
            resolutionType,
            userName: $"{label}.{TestRunId}",
            isAudited: null);

    private sealed class StubApplicationReader(ApplicationSummary? application)
        : IApplicationReader
    {
        public Task<IReadOnlyList<ApplicationSummary>> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicationSummary>>(
                application is null ? [] : [application]);

        public Task<ApplicationSummary?> FindByIdAsync(
            int applicationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                application?.ApplicationId == applicationId
                    ? application
                    : null);
    }

    private sealed class StubValidationRoleReader(params ValidationRole[] roles)
        : IValidationRoleReader
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ValidationRole>> GetByApplicationAsync(
            int applicationId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<ValidationRole>>(roles);
        }

        public Task<ValidationRole?> FindByIdAsync(
            int roleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(roles.SingleOrDefault(role => role.RoleId == roleId));
    }

    private sealed class StubRoleOwnerReader : IRoleOwnerReader
    {
        private readonly IReadOnlyList<RoleOwnerRecord> _owners;
        private readonly Exception? _failure;

        public StubRoleOwnerReader(params RoleOwnerRecord[] owners)
        {
            _owners = owners;
        }

        public StubRoleOwnerReader(Exception failure)
        {
            _owners = [];
            _failure = failure;
        }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<RoleOwnerRecord>> GetByApplicationAsync(
            int applicationId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _failure is null
                ? Task.FromResult(_owners)
                : Task.FromException<IReadOnlyList<RoleOwnerRecord>>(_failure);
        }
    }

    private sealed class StubEmployeeReader(params EmployeeRecord[] employees)
        : IEmployeeReader
    {
        public int EmployeeNoCallCount { get; private set; }

        public IReadOnlyList<string> LastEmployeeNos { get; private set; } = [];

        public Task<IReadOnlyList<EmployeeRecord>> FindByEmployeeNosAsync(
            IReadOnlyCollection<string> employeeNos,
            CancellationToken cancellationToken = default)
        {
            EmployeeNoCallCount++;
            LastEmployeeNos = employeeNos.ToArray();
            IReadOnlyList<EmployeeRecord> matches = employees
                .Where(employee => employeeNos.Contains(
                    employee.EmployeeNo,
                    StringComparer.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult(matches);
        }

        public Task<IReadOnlyList<EmployeeRecord>> FindByUserNamesAsync(
            IReadOnlyCollection<string> userNames,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EmployeeRecord>>([]);
    }

    private sealed class RecordingApplicationUserLoader(
        IReadOnlyList<ApplicationUserView> users)
    {
        public List<LoadApplicationUserRequest> Requests { get; } = [];

        public Task<LoadApplicationUserResult> LoadAsync(
            LoadApplicationUserRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(
                LoadApplicationUserResult.CreateSuccess(users));
        }
    }

    private sealed class RequestFilteringApplicationUserLoader(
        IReadOnlyList<ApplicationUserView> users)
    {
        public List<LoadApplicationUserRequest> Requests { get; } = [];

        public Task<LoadApplicationUserResult> LoadAsync(
            LoadApplicationUserRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            IEnumerable<ApplicationUserView> selected =
                request.MappedRoleSelectionMode switch
                {
                    MappedRoleSelectionMode.All => users,
                    MappedRoleSelectionMode.Selected => users.Where(user =>
                        user.RoleId.HasValue &&
                        request.MappedRoleIds.Contains(user.RoleId.Value)),
                    MappedRoleSelectionMode.None =>
                        Enumerable.Empty<ApplicationUserView>(),
                    _ => throw new InvalidOperationException()
                };
            selected = request.AuditedSelectionMode switch
            {
                FilterSelectionMode.All => selected,
                FilterSelectionMode.Selected => selected.Where(user =>
                    request.AuditedStatuses.Any(audited =>
                        audited
                            ? user.IsAudited != false
                            : user.IsAudited == false)),
                FilterSelectionMode.None =>
                    Enumerable.Empty<ApplicationUserView>(),
                _ => throw new InvalidOperationException()
            };
            selected = request.ResolutionSelectionMode switch
            {
                FilterSelectionMode.All => selected,
                FilterSelectionMode.Selected => selected.Where(user =>
                    request.ResolutionTypes.Contains(user.ResolutionType)),
                FilterSelectionMode.None =>
                    Enumerable.Empty<ApplicationUserView>(),
                _ => throw new InvalidOperationException()
            };
            return Task.FromResult(
                LoadApplicationUserResult.CreateSuccess(selected.ToArray()));
        }
    }
}
