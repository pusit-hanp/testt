using RoleValidation.Application.Employees;
using RoleValidation.Core.Features.RoleValidation;

namespace RoleValidation.Application.Tests.Employees;

public sealed class EmployeeIdentityResolverTests
{
    private static readonly string TestUserName = $"test.{Guid.NewGuid():N}";
    private static readonly string SharedUserName = $"{TestUserName}.shared";
    private static readonly string ContractorUserName = $"{TestUserName}.contractor";

    [Fact]
    public async Task ResolveAsync_Should_ReturnSingleActiveEmployee()
    {
        var employee = CreateEmployee(
            "62032665",
            TestUserName,
            "A",
            new DateTime(2026, 7, 1));
        var resolver = new EmployeeIdentityResolver(
            new StubEmployeeReader([employee]));

        EmployeeIdentityResolution result = await resolver.ResolveAsync(
            $" {TestUserName.ToUpperInvariant()} ");

        Assert.True(result.IsResolved);
        Assert.Same(employee, result.Employee);
        Assert.Null(result.FailureCode);
    }

    [Fact]
    public async Task ResolveAsync_Should_DiscardInactiveContractorBeforeSelectingPermanentEmployee()
    {
        var inactiveContractor = CreateEmployee(
            "C1001935",
            ContractorUserName,
            "D",
            new DateTime(2024, 11, 6));
        var activePermanent = CreateEmployee(
            "62032665",
            ContractorUserName,
            "A",
            new DateTime(2026, 7, 1));
        var resolver = new EmployeeIdentityResolver(
            new StubEmployeeReader([inactiveContractor, activePermanent]));

        EmployeeIdentityResolution result = await resolver.ResolveAsync(
            ContractorUserName);

        Assert.True(result.IsResolved);
        Assert.Same(activePermanent, result.Employee);
    }

    [Fact]
    public async Task ResolveAsync_Should_SelectUniqueLatestJoinDateWhenMultipleActiveRowsRemain()
    {
        var older = CreateEmployee(
            "62007377",
            SharedUserName,
            "A",
            new DateTime(2019, 7, 1));
        var latest = CreateEmployee(
            "62032665",
            SharedUserName,
            "A",
            new DateTime(2026, 7, 1));
        var unknownDate = CreateEmployee(
            "62009999",
            SharedUserName,
            "A",
            joinDate: null);
        var resolver = new EmployeeIdentityResolver(
            new StubEmployeeReader([older, latest, unknownDate]));

        EmployeeIdentityResolution result = await resolver.ResolveAsync(
            SharedUserName);

        Assert.True(result.IsResolved);
        Assert.Same(latest, result.Employee);
    }

    [Fact]
    public async Task ResolveAsync_Should_DenyWhenLatestJoinDateIsTied()
    {
        var joinDate = new DateTime(2026, 7, 1);
        var resolver = new EmployeeIdentityResolver(
            new StubEmployeeReader(
            [
                CreateEmployee("62032665", SharedUserName, "A", joinDate),
                CreateEmployee("62032666", SharedUserName, "A", joinDate)
            ]));

        EmployeeIdentityResolution result = await resolver.ResolveAsync(
            SharedUserName);

        Assert.False(result.IsResolved);
        Assert.Null(result.Employee);
        Assert.Equal("EMPLOYEE_AMBIGUOUS", result.FailureCode);
    }

    [Fact]
    public async Task ResolveAsync_Should_DenyWhenMultipleActiveRowsHaveNoJoinDate()
    {
        var resolver = new EmployeeIdentityResolver(
            new StubEmployeeReader(
            [
                CreateEmployee("62032665", SharedUserName, "A", null),
                CreateEmployee("62032666", SharedUserName, "A", null)
            ]));

        EmployeeIdentityResolution result = await resolver.ResolveAsync(
            SharedUserName);

        Assert.False(result.IsResolved);
        Assert.Equal("EMPLOYEE_AMBIGUOUS", result.FailureCode);
    }

    [Fact]
    public async Task ResolveAsync_Should_DenyWhenNoActiveEmployeeMatches()
    {
        var resolver = new EmployeeIdentityResolver(
            new StubEmployeeReader(
            [
                CreateEmployee(
                    "C1001935",
                    ContractorUserName,
                    "D",
                    new DateTime(2024, 11, 6))
            ]));

        EmployeeIdentityResolution result = await resolver.ResolveAsync(
            ContractorUserName);

        Assert.False(result.IsResolved);
        Assert.Equal("EMPLOYEE_NOT_RESOLVED", result.FailureCode);
    }

    [Fact]
    public async Task ResolveAsync_Should_ReturnUnavailableWhenEmployeeSourceFails()
    {
        var resolver = new EmployeeIdentityResolver(
            new StubEmployeeReader(new InvalidOperationException("unavailable")));

        EmployeeIdentityResolution result = await resolver.ResolveAsync(
            TestUserName);

        Assert.False(result.IsResolved);
        Assert.Equal("EMPLOYEE_LOOKUP_UNAVAILABLE", result.FailureCode);
    }

    [Theory]
    [InlineData("th26017476")]
    [InlineData("zzc1001935")]
    [InlineData("62032665")]
    public async Task ResolveAsync_Should_NotParseEmployeeNoFromUserName(
        string userName)
    {
        var resolver = new EmployeeIdentityResolver(
            StubEmployeeReader.WithEmployeeNumberTrap(
                CreateEmployee(
                    "62032665",
                    userName: null,
                    statusCode: "A",
                    joinDate: new DateTime(2026, 7, 1))));

        EmployeeIdentityResolution result = await resolver.ResolveAsync(userName);

        Assert.False(result.IsResolved);
        Assert.Equal("EMPLOYEE_NOT_RESOLVED", result.FailureCode);
    }

    private static EmployeeRecord CreateEmployee(
        string employeeNo,
        string? userName,
        string statusCode,
        DateTime? joinDate)
    {
        return new EmployeeRecord(
            employeeNo,
            employeeName: null,
            new EmployeeStatus(statusCode),
            userName: userName,
            joinDate: joinDate);
    }

    private sealed class StubEmployeeReader : IEmployeeReader
    {
        private readonly IReadOnlyList<EmployeeRecord> _userNameRecords;
        private readonly IReadOnlyList<EmployeeRecord> _employeeNumberRecords;
        private readonly Exception? _exception;

        public StubEmployeeReader(IReadOnlyList<EmployeeRecord> userNameRecords)
            : this(userNameRecords, [])
        {
        }

        public StubEmployeeReader(Exception exception)
            : this([], [], exception)
        {
        }

        private StubEmployeeReader(
            IReadOnlyList<EmployeeRecord> userNameRecords,
            IReadOnlyList<EmployeeRecord> employeeNumberRecords,
            Exception? exception = null)
        {
            _userNameRecords = userNameRecords;
            _employeeNumberRecords = employeeNumberRecords;
            _exception = exception;
        }

        public static StubEmployeeReader WithEmployeeNumberTrap(
            EmployeeRecord employee)
        {
            return new StubEmployeeReader([], [employee]);
        }

        public Task<IReadOnlyList<EmployeeRecord>> FindByEmployeeNosAsync(
            IReadOnlyCollection<string> employeeNos,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_employeeNumberRecords);
        }

        public Task<IReadOnlyList<EmployeeRecord>> FindByUserNamesAsync(
            IReadOnlyCollection<string> userNames,
            CancellationToken cancellationToken = default)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_userNameRecords);
        }
    }
}
