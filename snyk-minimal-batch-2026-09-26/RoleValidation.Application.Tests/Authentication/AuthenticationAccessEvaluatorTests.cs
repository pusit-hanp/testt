using RoleValidation.Application.Authentication;
using RoleValidation.Application.Employees;
using RoleValidation.Core.Features.Authorization;
using RoleValidation.Core.Features.RoleValidation;

namespace RoleValidation.Application.Tests.Authentication;

public sealed class AuthenticationAccessEvaluatorTests
{
    private static readonly string TestUserName = $"test.{Guid.NewGuid():N}";
    private static readonly string SharedUserName = $"{TestUserName}.shared";

    [Theory]
    [InlineData("Admin", "Admin")]
    [InlineData("Local_IT_Admin", "Local_IT_Admin")]
    public async Task EvaluateAsync_Should_AllowActiveSupportedRole(
        string storedRole,
        string expectedRole)
    {
        var employee = CreateEmployee("62032665", TestUserName, "A");
        var evaluator = CreateEvaluator(
            [employee],
            [new AuthorizedUserRecord("62032665", storedRole, isActive: true)]);

        AuthenticationAccessDecision result = await evaluator.EvaluateAsync(
            new ExternalIdentity($" {TestUserName.ToUpperInvariant()} "));

        Assert.True(result.IsAllowed);
        Assert.Same(employee, result.Employee);
        Assert.Equal(expectedRole, result.AccessRole?.Value);
        Assert.Null(result.FailureCode);
    }

    [Fact]
    public async Task EvaluateAsync_Should_PreferLocalItAdminWhenBothRolesAreActive()
    {
        var evaluator = CreateEvaluator(
            [CreateEmployee("62032665", TestUserName, "A")],
            [
                new AuthorizedUserRecord("62032665", "Admin", isActive: true),
                new AuthorizedUserRecord(
                    "62032665",
                    "Local_IT_Admin",
                    isActive: true)
            ]);

        AuthenticationAccessDecision result = await evaluator.EvaluateAsync(
            new ExternalIdentity(TestUserName));

        Assert.True(result.IsAllowed);
        Assert.Equal(AccessRole.LocalItAdmin, result.AccessRole);
    }

    [Fact]
    public async Task EvaluateAsync_Should_DenyMissingAuthorizedUser()
    {
        var evaluator = CreateEvaluator(
            [CreateEmployee("62032665", TestUserName, "A")],
            []);

        AuthenticationAccessDecision result = await evaluator.EvaluateAsync(
            new ExternalIdentity(TestUserName));

        Assert.False(result.IsAllowed);
        Assert.Equal("USER_NOT_AUTHORIZED", result.FailureCode);
    }

    [Fact]
    public async Task EvaluateAsync_Should_DenyInactiveAuthorizedUser()
    {
        var evaluator = CreateEvaluator(
            [CreateEmployee("62032665", TestUserName, "A")],
            [new AuthorizedUserRecord("62032665", "Admin", isActive: false)]);

        AuthenticationAccessDecision result = await evaluator.EvaluateAsync(
            new ExternalIdentity(TestUserName));

        Assert.False(result.IsAllowed);
        Assert.Equal("USER_INACTIVE", result.FailureCode);
    }

    [Fact]
    public async Task EvaluateAsync_Should_DenyUnsupportedActiveRole()
    {
        var evaluator = CreateEvaluator(
            [CreateEmployee("62032665", TestUserName, "A")],
            [new AuthorizedUserRecord(
                "62032665",
                "Role_Owner",
                isActive: true)]);

        AuthenticationAccessDecision result = await evaluator.EvaluateAsync(
            new ExternalIdentity(TestUserName));

        Assert.False(result.IsAllowed);
        Assert.Equal("ACCESS_ROLE_UNSUPPORTED", result.FailureCode);
    }

    [Theory]
    [InlineData("D", "EMPLOYEE_NOT_RESOLVED")]
    [InlineData("A", "EMPLOYEE_AMBIGUOUS")]
    public async Task EvaluateAsync_Should_KeepEmployeeResolutionFailureCode(
        string statusCode,
        string expectedFailureCode)
    {
        IReadOnlyList<EmployeeRecord> employees = statusCode == "D"
            ? [CreateEmployee("C1001935", SharedUserName, "D")]
            :
            [
                CreateEmployee(
                    "62032665",
                    SharedUserName,
                    "A",
                    new DateTime(2026, 7, 1)),
                CreateEmployee(
                    "62032666",
                    SharedUserName,
                    "A",
                    new DateTime(2026, 7, 1))
            ];
        var evaluator = CreateEvaluator(employees, []);

        AuthenticationAccessDecision result = await evaluator.EvaluateAsync(
            new ExternalIdentity(SharedUserName));

        Assert.False(result.IsAllowed);
        Assert.Equal(expectedFailureCode, result.FailureCode);
    }

    [Fact]
    public async Task EvaluateAsync_Should_DenyWhenEmployeeSourceIsUnavailable()
    {
        var resolver = new EmployeeIdentityResolver(
            new ThrowingEmployeeReader());
        var evaluator = new AuthenticationAccessEvaluator(
            resolver,
            new StubAuthorizedUserReader([]));

        AuthenticationAccessDecision result = await evaluator.EvaluateAsync(
            new ExternalIdentity(TestUserName));

        Assert.False(result.IsAllowed);
        Assert.Equal("EMPLOYEE_LOOKUP_UNAVAILABLE", result.FailureCode);
    }

    [Fact]
    public async Task EvaluateAsync_Should_DenyWhenAuthorizationSourceIsUnavailable()
    {
        var resolver = new EmployeeIdentityResolver(
            new StubEmployeeReader(
                [CreateEmployee("62032665", TestUserName, "A")]));
        var evaluator = new AuthenticationAccessEvaluator(
            resolver,
            new StubAuthorizedUserReader(
                new InvalidOperationException("unavailable")));

        AuthenticationAccessDecision result = await evaluator.EvaluateAsync(
            new ExternalIdentity(TestUserName));

        Assert.False(result.IsAllowed);
        Assert.Equal("62032665", result.Employee?.EmployeeNo);
        Assert.Equal("AUTHORIZATION_SOURCE_UNAVAILABLE", result.FailureCode);
    }

    [Theory]
    [InlineData("th26017476")]
    [InlineData("zzc1001935")]
    [InlineData("62032665")]
    public async Task EvaluateAsync_Should_NotDeriveEmployeeNoFromUserName(
        string userName)
    {
        var employeeReader = new EmployeeNumberTrapReader(
            CreateEmployee(
                "62032665",
                userName: null,
                statusCode: "A"));
        var evaluator = new AuthenticationAccessEvaluator(
            new EmployeeIdentityResolver(employeeReader),
            new StubAuthorizedUserReader(
                [new AuthorizedUserRecord("62032665", "Admin", true)]));

        AuthenticationAccessDecision result = await evaluator.EvaluateAsync(
            new ExternalIdentity(userName));

        Assert.False(result.IsAllowed);
        Assert.Equal("EMPLOYEE_NOT_RESOLVED", result.FailureCode);
    }

    private static AuthenticationAccessEvaluator CreateEvaluator(
        IReadOnlyList<EmployeeRecord> employees,
        IReadOnlyList<AuthorizedUserRecord> authorizedUsers)
    {
        return new AuthenticationAccessEvaluator(
            new EmployeeIdentityResolver(new StubEmployeeReader(employees)),
            new StubAuthorizedUserReader(authorizedUsers));
    }

    private static EmployeeRecord CreateEmployee(
        string employeeNo,
        string? userName,
        string statusCode,
        DateTime? joinDate = null)
    {
        return new EmployeeRecord(
            employeeNo,
            employeeName: null,
            new EmployeeStatus(statusCode),
            userName: userName,
            joinDate: joinDate);
    }

    private sealed class StubAuthorizedUserReader : IAuthorizedUserReader
    {
        private readonly IReadOnlyList<AuthorizedUserRecord> _records;
        private readonly Exception? _exception;

        public StubAuthorizedUserReader(
            IReadOnlyList<AuthorizedUserRecord> records)
        {
            _records = records;
        }

        public StubAuthorizedUserReader(Exception exception)
        {
            _records = [];
            _exception = exception;
        }

        public Task<IReadOnlyList<AuthorizedUserRecord>> FindByEmployeeNoAsync(
            string employeeNo,
            CancellationToken cancellationToken = default)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_records);
        }
    }

    private class StubEmployeeReader : IEmployeeReader
    {
        private readonly IReadOnlyList<EmployeeRecord> _records;

        public StubEmployeeReader(IReadOnlyList<EmployeeRecord> records)
        {
            _records = records;
        }

        public virtual Task<IReadOnlyList<EmployeeRecord>> FindByEmployeeNosAsync(
            IReadOnlyCollection<string> employeeNos,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<EmployeeRecord>>([]);
        }

        public virtual Task<IReadOnlyList<EmployeeRecord>> FindByUserNamesAsync(
            IReadOnlyCollection<string> userNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records);
        }
    }

    private sealed class EmployeeNumberTrapReader : StubEmployeeReader
    {
        private readonly EmployeeRecord _employee;

        public EmployeeNumberTrapReader(EmployeeRecord employee)
            : base([])
        {
            _employee = employee;
        }

        public override Task<IReadOnlyList<EmployeeRecord>> FindByEmployeeNosAsync(
            IReadOnlyCollection<string> employeeNos,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<EmployeeRecord>>([_employee]);
        }
    }

    private sealed class ThrowingEmployeeReader : IEmployeeReader
    {
        public Task<IReadOnlyList<EmployeeRecord>> FindByEmployeeNosAsync(
            IReadOnlyCollection<string> employeeNos,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("unavailable");
        }

        public Task<IReadOnlyList<EmployeeRecord>> FindByUserNamesAsync(
            IReadOnlyCollection<string> userNames,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("unavailable");
        }
    }
}
