using RoleValidation.Application.Authentication;
using RoleValidation.Application.Employees;
using RoleValidation.Core.Features.Authorization;
using RoleValidation.Core.Features.RoleValidation;
using RoleValidation.Infrastructure.Security;
using RoleValidation.Web.Authentication;

namespace RoleValidation.Web.Tests.Authentication;

public sealed class AuthenticationFlowServiceTests
{
    private static readonly string TestUserName = $"test.{Guid.NewGuid():N}";

    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        27,
        3,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task AuthenticateAsync_Should_RecordSuccessOnceAfterAllowListApproval()
    {
        var recorder = new RecordingLoginAccessRecorder();
        AuthenticationFlowService service = CreateService(
            [CreateEmployee("62032665", TestUserName)],
            [new AuthorizedUserRecord("62032665", "Admin", true)],
            recorder);

        AuthenticationFlowResult result = await service.AuthenticateAsync(
            CreateValidResponse(),
            "CORR-001");

        Assert.True(result.IsAllowed);
        Assert.Equal("62032665", result.Employee?.EmployeeNo);
        Assert.Equal(AccessRole.Admin, result.AccessRole);
        LoginAccessEvent loginEvent = Assert.Single(recorder.Events);
        Assert.Equal(LoginAccessResult.Success, loginEvent.Result);
        Assert.Equal("62032665", loginEvent.EmployeeNo);
        Assert.Equal("CORR-001", loginEvent.CorrelationId);
    }

    [Fact]
    public async Task AuthenticateAsync_Should_RecordDeniedAfterAllowListRejection()
    {
        var recorder = new RecordingLoginAccessRecorder();
        AuthenticationFlowService service = CreateService(
            [CreateEmployee("62032665", TestUserName)],
            [],
            recorder);

        AuthenticationFlowResult result = await service.AuthenticateAsync(
            CreateValidResponse(),
            "CORR-002");

        Assert.False(result.IsAllowed);
        Assert.Equal("USER_NOT_AUTHORIZED", result.FailureCode);
        LoginAccessEvent loginEvent = Assert.Single(recorder.Events);
        Assert.Equal(LoginAccessResult.Denied, loginEvent.Result);
        Assert.Equal("62032665", loginEvent.EmployeeNo);
        Assert.Equal("USER_NOT_AUTHORIZED", loginEvent.FailureCode);
        Assert.Equal("CORR-002", loginEvent.CorrelationId);
    }

    [Fact]
    public async Task AuthenticateAsync_Should_RecordInvalidCallbackWithoutEmployeeNo()
    {
        var recorder = new RecordingLoginAccessRecorder();
        AuthenticationFlowService service = CreateService(
            employees: [],
            authorizedUsers: [],
            recorder);

        AuthenticationFlowResult result = await service.AuthenticateAsync(
            "not-base64",
            "CORR-003");

        Assert.False(result.IsAllowed);
        Assert.Equal("CALLBACK_INVALID", result.FailureCode);
        LoginAccessEvent loginEvent = Assert.Single(recorder.Events);
        Assert.Equal(LoginAccessResult.Denied, loginEvent.Result);
        Assert.Null(loginEvent.EmployeeNo);
        Assert.Equal("CALLBACK_INVALID", loginEvent.FailureCode);
        Assert.Equal("CORR-003", loginEvent.CorrelationId);
    }

    [Fact]
    public async Task RecordDeniedAsync_Should_RecordStateFailureWithoutEmployeeNo()
    {
        var recorder = new RecordingLoginAccessRecorder();
        AuthenticationFlowService service = CreateService(
            employees: [],
            authorizedUsers: [],
            recorder);

        await service.RecordDeniedAsync(
            "CORR-STATE",
            "CALLBACK_REPLAYED");

        LoginAccessEvent loginEvent = Assert.Single(recorder.Events);
        Assert.Equal(LoginAccessResult.Denied, loginEvent.Result);
        Assert.Null(loginEvent.EmployeeNo);
        Assert.Equal("CALLBACK_REPLAYED", loginEvent.FailureCode);
        Assert.Equal("CORR-STATE", loginEvent.CorrelationId);
        Assert.Equal(Now, loginEvent.OccurredAt);
    }

    private static AuthenticationFlowService CreateService(
        IReadOnlyList<EmployeeRecord> employees,
        IReadOnlyList<AuthorizedUserRecord> authorizedUsers,
        RecordingLoginAccessRecorder recorder)
    {
        var timeProvider = new FixedTimeProvider(Now);
        var encryption = new AesTextEncryptionService("safe-test-passphrase");
        var validator = new CompanyLoginResponseValidator(
            encryption,
            new CompanyLoginOptions
            {
                ResponseLifetimeMinutes = 5,
                ClockSkewSeconds = 30
            },
            timeProvider);
        var evaluator = new AuthenticationAccessEvaluator(
            new EmployeeIdentityResolver(new StubEmployeeReader(employees)),
            new StubAuthorizedUserReader(authorizedUsers));

        return new AuthenticationFlowService(
            validator,
            evaluator,
            recorder,
            timeProvider);
    }

    private static string CreateValidResponse()
    {
        return new AesTextEncryptionService("safe-test-passphrase").Encrypt(
            $"$user={TestUserName},fname=Person,lname=User," +
            "mail=person.user@example.com,time=08/27/2026 10:04:00");
    }

    private static EmployeeRecord CreateEmployee(
        string employeeNo,
        string userName)
    {
        return new EmployeeRecord(
            employeeNo,
            "Person User",
            new EmployeeStatus("A"),
            email: "person.user@example.com",
            userName: userName,
            joinDate: new DateTime(2026, 7, 1));
    }

    private sealed class RecordingLoginAccessRecorder : ILoginAccessRecorder
    {
        public List<LoginAccessEvent> Events { get; } = [];

        public Task RecordAsync(
            LoginAccessEvent loginAccessEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(loginAccessEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthorizedUserReader : IAuthorizedUserReader
    {
        private readonly IReadOnlyList<AuthorizedUserRecord> _records;

        public StubAuthorizedUserReader(
            IReadOnlyList<AuthorizedUserRecord> records)
        {
            _records = records;
        }

        public Task<IReadOnlyList<AuthorizedUserRecord>> FindByEmployeeNoAsync(
            string employeeNo,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records);
        }
    }

    private sealed class StubEmployeeReader : IEmployeeReader
    {
        private readonly IReadOnlyList<EmployeeRecord> _records;

        public StubEmployeeReader(IReadOnlyList<EmployeeRecord> records)
        {
            _records = records;
        }

        public Task<IReadOnlyList<EmployeeRecord>> FindByEmployeeNosAsync(
            IReadOnlyCollection<string> employeeNos,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<EmployeeRecord>>([]);
        }

        public Task<IReadOnlyList<EmployeeRecord>> FindByUserNamesAsync(
            IReadOnlyCollection<string> userNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone(
                "TestTimeZone",
                TimeSpan.FromHours(7),
                "TestTimeZone",
                "TestTimeZone");

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
