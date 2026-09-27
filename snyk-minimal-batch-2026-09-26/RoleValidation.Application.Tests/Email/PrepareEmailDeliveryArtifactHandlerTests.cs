using Moq;
using RoleValidation.Application.Email;
using RoleValidation.Application.RoleValidation;
using RoleValidation.Core.Features.Email;
using RoleValidation.Core.Features.SourceMappings;

namespace RoleValidation.Application.Tests.Email;

public sealed class PrepareEmailDeliveryArtifactHandlerTests
{
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 8, 31, 4, 5, 6, TimeSpan.Zero);

    [Fact]
    public async Task PrepareAsync_Should_HonorPreCancelledTokenWithoutTouchingDependencies()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var handler = new PrepareEmailDeliveryArtifactHandler(
            Mock.Of<IOwnerScopeBuilder>(MockBehavior.Strict),
            Mock.Of<IOwnerWorkbookExporter>(MockBehavior.Strict),
            Mock.Of<IEmailArtifactStore>(MockBehavior.Strict),
            Mock.Of<IEmailExecutionStore>(MockBehavior.Strict),
            new FixedTimeProvider(GeneratedAt));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.PrepareAsync(WorkItem(), source.Token));
    }

    [Fact]
    public async Task PrepareAsync_Should_ReuseValidPersistedWorkbookWithoutRebuilding()
    {
        EmailArtifactMetadata attachment = Artifact("artifact-00000000000000000000000000000001.xlsx");
        var artifacts = new Mock<IEmailArtifactStore>(MockBehavior.Strict);
        artifacts.Setup(store => store.ReadOwnerWorkbookAsync(
                901,
                71,
                attachment,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([1, 2, 3]);
        var handler = new PrepareEmailDeliveryArtifactHandler(
            Mock.Of<IOwnerScopeBuilder>(MockBehavior.Strict),
            Mock.Of<IOwnerWorkbookExporter>(MockBehavior.Strict),
            artifacts.Object,
            Mock.Of<IEmailExecutionStore>(MockBehavior.Strict),
            new FixedTimeProvider(GeneratedAt));

        PrepareEmailDeliveryArtifactResult result = await handler.PrepareAsync(
            WorkItem(attachment));

        Assert.Equal(PrepareEmailDeliveryArtifactOutcome.Ready, result.Outcome);
        Assert.Equal(attachment, result.Artifact);
        Assert.Null(result.ErrorCode);
        artifacts.VerifyAll();
    }

    [Fact]
    public async Task PrepareAsync_Should_FailClosedForInvalidPersistedWorkbook()
    {
        EmailArtifactMetadata attachment = Artifact("artifact-00000000000000000000000000000001.xlsx");
        var artifacts = new Mock<IEmailArtifactStore>(MockBehavior.Strict);
        artifacts.Setup(store => store.ReadOwnerWorkbookAsync(
                901,
                71,
                attachment,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("outside approved root"));
        var handler = new PrepareEmailDeliveryArtifactHandler(
            Mock.Of<IOwnerScopeBuilder>(MockBehavior.Strict),
            Mock.Of<IOwnerWorkbookExporter>(MockBehavior.Strict),
            artifacts.Object,
            Mock.Of<IEmailExecutionStore>(MockBehavior.Strict),
            new FixedTimeProvider(GeneratedAt));

        PrepareEmailDeliveryArtifactResult result = await handler.PrepareAsync(
            WorkItem(attachment));

        Assert.Equal(PrepareEmailDeliveryArtifactOutcome.Failed, result.Outcome);
        Assert.Null(result.Artifact);
        Assert.Equal("EMAIL_ARTIFACT_INVALID", result.ErrorCode);
        artifacts.VerifyAll();
    }

    [Fact]
    public async Task PrepareAsync_Should_ExportLatestIntendedOwnerScopeWithStoredActor()
    {
        OwnerScope intendedScope = Scope(
            "C1000001",
            roleIds: [10],
            rows: [User(10)]);
        OwnerScope otherScope = Scope(
            "C1000002",
            roleIds: [11],
            rows: [User(11)]);
        var calls = new List<string>();
        var builder = new Mock<IOwnerScopeBuilder>(MockBehavior.Strict);
        builder.Setup(item => item.BuildAsync(17, It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((_, _) => calls.Add("scope"))
            .ReturnsAsync(new OwnerScopeBuildResult(
                17,
                "ERSN",
                "eRSN",
                [intendedScope, otherScope]));
        var exporter = new Mock<IOwnerWorkbookExporter>(MockBehavior.Strict);
        exporter.Setup(item => item.Export(
                intendedScope,
                GeneratedAt,
                "C1008267"))
            .Callback<OwnerScope, DateTimeOffset, string>((_, _, _) => calls.Add("export"))
            .Returns([8, 9]);
        EmailArtifactMetadata published = Artifact("artifact-00000000000000000000000000000002.xlsx");
        var artifacts = new Mock<IEmailArtifactStore>(MockBehavior.Strict);
        artifacts.Setup(item => item.PublishOwnerWorkbookAsync(
                901,
                71,
                It.Is<byte[]>(content => content.SequenceEqual(
                    new byte[] { 8, 9 })),
                It.IsAny<CancellationToken>()))
            .Callback<long, long, byte[], CancellationToken>((_, _, _, _) => calls.Add("publish"))
            .ReturnsAsync(published);
        var store = new Mock<IEmailExecutionStore>(MockBehavior.Strict);
        store.Setup(item => item.TryPersistDeliveryArtifactAsync(
                It.Is<EmailDeliveryAttemptLease>(lease =>
                    lease == WorkItem().AttemptLease),
                published,
                It.IsAny<CancellationToken>()))
            .Callback<EmailDeliveryAttemptLease, EmailArtifactMetadata, CancellationToken>(
                (_, _, _) => calls.Add("persist"))
            .ReturnsAsync(ConditionalEmailMetadataWriteOutcome.Applied);
        var handler = new PrepareEmailDeliveryArtifactHandler(
            builder.Object,
            exporter.Object,
            artifacts.Object,
            store.Object,
            new FixedTimeProvider(GeneratedAt));

        PrepareEmailDeliveryArtifactResult result = await handler.PrepareAsync(
            WorkItem(workbookExportedBy: "C1008267"));

        Assert.Equal(PrepareEmailDeliveryArtifactOutcome.Ready, result.Outcome);
        Assert.Equal(published, result.Artifact);
        Assert.Null(result.ErrorCode);
        Assert.Equal(["scope", "export", "publish", "persist"], calls);
        builder.Verify(item => item.BuildAsync(17, It.IsAny<CancellationToken>()), Times.Once);
        exporter.Verify(item => item.Export(intendedScope, GeneratedAt, "C1008267"), Times.Once);
        artifacts.Verify(item => item.PublishOwnerWorkbookAsync(
            901, 71, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(item => item.TryPersistDeliveryArtifactAsync(
            It.IsAny<EmailDeliveryAttemptLease>(), published, It.IsAny<CancellationToken>()), Times.Once);
        builder.VerifyAll();
        exporter.VerifyAll();
        artifacts.VerifyAll();
        store.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareAsync_Should_ReturnOwnerScopeChangedWhenIntendedOwnerIsMissingOrHasNoRoles(
        bool includeOwner)
    {
        OwnerScope[] scopes = includeOwner
            ? [Scope("C1000001", roleIds: [], rows: [])]
            : [];
        var builder = new Mock<IOwnerScopeBuilder>(MockBehavior.Strict);
        builder.Setup(item => item.BuildAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnerScopeBuildResult(17, "ERSN", "eRSN", scopes));
        var handler = new PrepareEmailDeliveryArtifactHandler(
            builder.Object,
            Mock.Of<IOwnerWorkbookExporter>(MockBehavior.Strict),
            Mock.Of<IEmailArtifactStore>(MockBehavior.Strict),
            Mock.Of<IEmailExecutionStore>(MockBehavior.Strict),
            new FixedTimeProvider(GeneratedAt));

        PrepareEmailDeliveryArtifactResult result = await handler.PrepareAsync(
            WorkItem());

        Assert.Equal(PrepareEmailDeliveryArtifactOutcome.Failed, result.Outcome);
        Assert.Null(result.Artifact);
        Assert.Equal("OWNER_SCOPE_CHANGED", result.ErrorCode);
        builder.VerifyAll();
    }

    [Fact]
    public async Task PrepareAsync_Should_PreserveFailedOwnerSeedCodeWithoutPublishing()
    {
        OwnerScope failedScope = Scope(
            "C1000001",
            roleIds: [],
            rows: [],
            seed: OwnerDeliverySeed.Failed("C1000001", "OWNER_INACTIVE"));
        var builder = new Mock<IOwnerScopeBuilder>(MockBehavior.Strict);
        builder.Setup(item => item.BuildAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnerScopeBuildResult(17, "ERSN", "eRSN", [failedScope]));
        var handler = new PrepareEmailDeliveryArtifactHandler(
            builder.Object,
            Mock.Of<IOwnerWorkbookExporter>(MockBehavior.Strict),
            Mock.Of<IEmailArtifactStore>(MockBehavior.Strict),
            Mock.Of<IEmailExecutionStore>(MockBehavior.Strict),
            new FixedTimeProvider(GeneratedAt));

        PrepareEmailDeliveryArtifactResult result = await handler.PrepareAsync(
            WorkItem());

        Assert.Equal(PrepareEmailDeliveryArtifactOutcome.Failed, result.Outcome);
        Assert.Null(result.Artifact);
        Assert.Equal("OWNER_INACTIVE", result.ErrorCode);
        builder.VerifyAll();
    }

    [Fact]
    public async Task PrepareAsync_Should_RejectDuplicateIntendedOwnerScopeAsInvalidDependencyData()
    {
        OwnerScope scope = Scope("C1000001", roleIds: [10], rows: []);
        var builder = new Mock<IOwnerScopeBuilder>(MockBehavior.Strict);
        builder.Setup(item => item.BuildAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnerScopeBuildResult(17, "ERSN", "eRSN", [scope, scope]));
        var handler = new PrepareEmailDeliveryArtifactHandler(
            builder.Object,
            Mock.Of<IOwnerWorkbookExporter>(MockBehavior.Strict),
            Mock.Of<IEmailArtifactStore>(MockBehavior.Strict),
            Mock.Of<IEmailExecutionStore>(MockBehavior.Strict),
            new FixedTimeProvider(GeneratedAt));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            handler.PrepareAsync(WorkItem()));

        builder.VerifyAll();
    }

    [Theory]
    [InlineData(ConditionalEmailMetadataWriteOutcome.Applied)]
    [InlineData(ConditionalEmailMetadataWriteOutcome.AlreadyMatched)]
    public async Task PrepareAsync_Should_ReturnReadyWhenConditionalPersistenceSucceeds(
        ConditionalEmailMetadataWriteOutcome outcome)
    {
        OwnerScope scope = Scope("C1000001", roleIds: [10], rows: []);
        var builder = new Mock<IOwnerScopeBuilder>(MockBehavior.Strict);
        builder.Setup(item => item.BuildAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnerScopeBuildResult(17, "ERSN", "eRSN", [scope]));
        var exporter = new Mock<IOwnerWorkbookExporter>(MockBehavior.Strict);
        exporter.Setup(item => item.Export(scope, GeneratedAt, "SYSTEM"))
            .Returns([1]);
        EmailArtifactMetadata artifact = Artifact("artifact-00000000000000000000000000000003.xlsx");
        var artifacts = new Mock<IEmailArtifactStore>(MockBehavior.Strict);
        artifacts.Setup(item => item.PublishOwnerWorkbookAsync(
                901,
                71,
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifact);
        var store = new Mock<IEmailExecutionStore>(MockBehavior.Strict);
        store.Setup(item => item.TryPersistDeliveryArtifactAsync(
                It.IsAny<EmailDeliveryAttemptLease>(),
                artifact,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        var handler = new PrepareEmailDeliveryArtifactHandler(
            builder.Object,
            exporter.Object,
            artifacts.Object,
            store.Object,
            new FixedTimeProvider(GeneratedAt));

        PrepareEmailDeliveryArtifactResult result = await handler.PrepareAsync(
            WorkItem());

        Assert.Equal(PrepareEmailDeliveryArtifactOutcome.Ready, result.Outcome);
        Assert.Equal(artifact, result.Artifact);
        Assert.Null(result.ErrorCode);
        builder.VerifyAll();
        exporter.VerifyAll();
        artifacts.VerifyAll();
        store.VerifyAll();
    }

    [Fact]
    public async Task PrepareAsync_Should_RejectStaleConditionalPersistenceWithoutExposingNewArtifact()
    {
        OwnerScope scope = Scope("C1000001", roleIds: [10], rows: []);
        var builder = new Mock<IOwnerScopeBuilder>(MockBehavior.Strict);
        builder.Setup(item => item.BuildAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnerScopeBuildResult(17, "ERSN", "eRSN", [scope]));
        var exporter = new Mock<IOwnerWorkbookExporter>(MockBehavior.Strict);
        exporter.Setup(item => item.Export(scope, GeneratedAt, "SYSTEM"))
            .Returns([1]);
        EmailArtifactMetadata artifact = Artifact("artifact-00000000000000000000000000000004.xlsx");
        var artifacts = new Mock<IEmailArtifactStore>(MockBehavior.Strict);
        artifacts.Setup(item => item.PublishOwnerWorkbookAsync(
                901,
                71,
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifact);
        var store = new Mock<IEmailExecutionStore>(MockBehavior.Strict);
        store.Setup(item => item.TryPersistDeliveryArtifactAsync(
                It.IsAny<EmailDeliveryAttemptLease>(),
                artifact,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConditionalEmailMetadataWriteOutcome.Rejected);
        var handler = new PrepareEmailDeliveryArtifactHandler(
            builder.Object,
            exporter.Object,
            artifacts.Object,
            store.Object,
            new FixedTimeProvider(GeneratedAt));

        PrepareEmailDeliveryArtifactResult result = await handler.PrepareAsync(
            WorkItem());

        Assert.Equal(PrepareEmailDeliveryArtifactOutcome.Rejected, result.Outcome);
        Assert.Null(result.Artifact);
        Assert.Equal("DELIVERY_ARTIFACT_WRITE_REJECTED", result.ErrorCode);
        builder.VerifyAll();
        exporter.VerifyAll();
        artifacts.VerifyAll();
        store.VerifyAll();
    }

    [Theory]
    [InlineData(0, 901, 17, "SYSTEM")]
    [InlineData(71, 0, 17, "SYSTEM")]
    [InlineData(71, 901, 0, "SYSTEM")]
    [InlineData(71, 901, 17, " ")]
    public async Task PrepareAsync_Should_RejectInvalidWorkItemBeforeDependencies(
        long deliveryId,
        long runId,
        int applicationId,
        string exportedBy)
    {
        var handler = new PrepareEmailDeliveryArtifactHandler(
            Mock.Of<IOwnerScopeBuilder>(MockBehavior.Strict),
            Mock.Of<IOwnerWorkbookExporter>(MockBehavior.Strict),
            Mock.Of<IEmailArtifactStore>(MockBehavior.Strict),
            Mock.Of<IEmailExecutionStore>(MockBehavior.Strict),
            new FixedTimeProvider(GeneratedAt));
        EmailDeliveryWorkItem workItem = new(
            deliveryId,
            runId,
            applicationId,
            "C1000001",
            "C1008267",
            "HYBRID",
            "FAKE",
            "SAFE_REDIRECT",
            2,
            GeneratedAt,
            exportedBy);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => handler.PrepareAsync(workItem));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrepareAsync_Should_RejectMismatchedBuildOrScopeApplicationIdentity(
        bool mismatchBuildIdentity)
    {
        OwnerScope scope = new(
            mismatchBuildIdentity ? 17 : 18,
            "ERSN",
            "eRSN",
            "C1000001",
            OwnerDeliverySeed.Pending("C1000001"),
            [10],
            []);
        var builder = new Mock<IOwnerScopeBuilder>(MockBehavior.Strict);
        builder.Setup(item => item.BuildAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnerScopeBuildResult(
                mismatchBuildIdentity ? 18 : 17,
                "ERSN",
                "eRSN",
                [scope]));
        var handler = new PrepareEmailDeliveryArtifactHandler(
            builder.Object,
            Mock.Of<IOwnerWorkbookExporter>(MockBehavior.Strict),
            Mock.Of<IEmailArtifactStore>(MockBehavior.Strict),
            Mock.Of<IEmailExecutionStore>(MockBehavior.Strict),
            new FixedTimeProvider(GeneratedAt));

        await Assert.ThrowsAsync<InvalidDataException>(() => handler.PrepareAsync(WorkItem()));

        builder.VerifyAll();
    }

    [Theory]
    [InlineData("OWNER_INACTIVE")]
    [InlineData("OWNER_EMPLOYEE_NOT_FOUND")]
    [InlineData("OWNER_EMPLOYEE_AMBIGUOUS")]
    [InlineData("OWNER_EMPLOYEE_STATUS_UNKNOWN")]
    public async Task PrepareAsync_Should_PreserveEveryFailedOwnerSeedCode(
        string errorCode)
    {
        OwnerScope failedScope = Scope(
            "C1000001",
            roleIds: [],
            rows: [],
            seed: OwnerDeliverySeed.Failed("C1000001", errorCode));
        var builder = new Mock<IOwnerScopeBuilder>(MockBehavior.Strict);
        builder.Setup(item => item.BuildAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnerScopeBuildResult(17, "ERSN", "eRSN", [failedScope]));
        var handler = new PrepareEmailDeliveryArtifactHandler(
            builder.Object,
            Mock.Of<IOwnerWorkbookExporter>(MockBehavior.Strict),
            Mock.Of<IEmailArtifactStore>(MockBehavior.Strict),
            Mock.Of<IEmailExecutionStore>(MockBehavior.Strict),
            new FixedTimeProvider(GeneratedAt));

        PrepareEmailDeliveryArtifactResult result = await handler.PrepareAsync(WorkItem());

        Assert.Equal(PrepareEmailDeliveryArtifactOutcome.Failed, result.Outcome);
        Assert.Null(result.Artifact);
        Assert.Equal(errorCode, result.ErrorCode);
        builder.VerifyAll();
    }

    [Fact]
    public async Task PrepareAsync_Should_PropagateExporterFailureWithoutPublishing()
    {
        OwnerScope scope = Scope("C1000001", [10], []);
        var builder = new Mock<IOwnerScopeBuilder>(MockBehavior.Strict);
        builder.Setup(item => item.BuildAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnerScopeBuildResult(17, "ERSN", "eRSN", [scope]));
        var exporter = new Mock<IOwnerWorkbookExporter>(MockBehavior.Strict);
        exporter.Setup(item => item.Export(scope, GeneratedAt, "SYSTEM"))
            .Throws(new InvalidOperationException("export failed"));
        var handler = new PrepareEmailDeliveryArtifactHandler(
            builder.Object,
            exporter.Object,
            Mock.Of<IEmailArtifactStore>(MockBehavior.Strict),
            Mock.Of<IEmailExecutionStore>(MockBehavior.Strict),
            new FixedTimeProvider(GeneratedAt));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.PrepareAsync(WorkItem()));

        builder.VerifyAll();
        exporter.VerifyAll();
    }

    [Fact]
    public async Task PrepareAsync_Should_PropagatePublicationFailureWithoutPersisting()
    {
        OwnerScope scope = Scope("C1000001", [10], []);
        var builder = new Mock<IOwnerScopeBuilder>(MockBehavior.Strict);
        builder.Setup(item => item.BuildAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnerScopeBuildResult(17, "ERSN", "eRSN", [scope]));
        var exporter = new Mock<IOwnerWorkbookExporter>(MockBehavior.Strict);
        exporter.Setup(item => item.Export(scope, GeneratedAt, "SYSTEM")).Returns([1]);
        var artifacts = new Mock<IEmailArtifactStore>(MockBehavior.Strict);
        artifacts.Setup(item => item.PublishOwnerWorkbookAsync(
                901, 71, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("write failed"));
        var handler = new PrepareEmailDeliveryArtifactHandler(
            builder.Object,
            exporter.Object,
            artifacts.Object,
            Mock.Of<IEmailExecutionStore>(MockBehavior.Strict),
            new FixedTimeProvider(GeneratedAt));

        await Assert.ThrowsAsync<IOException>(() => handler.PrepareAsync(WorkItem()));

        builder.VerifyAll();
        exporter.VerifyAll();
        artifacts.VerifyAll();
    }

    [Fact]
    public async Task PrepareAsync_Should_PropagatePersistenceFailureAfterPublishing()
    {
        OwnerScope scope = Scope("C1000001", [10], []);
        EmailArtifactMetadata published = Artifact("artifact-00000000000000000000000000000021.xlsx");
        var builder = new Mock<IOwnerScopeBuilder>(MockBehavior.Strict);
        builder.Setup(item => item.BuildAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OwnerScopeBuildResult(17, "ERSN", "eRSN", [scope]));
        var exporter = new Mock<IOwnerWorkbookExporter>(MockBehavior.Strict);
        exporter.Setup(item => item.Export(scope, GeneratedAt, "SYSTEM")).Returns([1]);
        var artifacts = new Mock<IEmailArtifactStore>(MockBehavior.Strict);
        artifacts.Setup(item => item.PublishOwnerWorkbookAsync(
                901, 71, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(published);
        var store = new Mock<IEmailExecutionStore>(MockBehavior.Strict);
        store.Setup(item => item.TryPersistDeliveryArtifactAsync(
                WorkItem().AttemptLease, published, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("persistence failed"));
        var handler = new PrepareEmailDeliveryArtifactHandler(
            builder.Object,
            exporter.Object,
            artifacts.Object,
            store.Object,
            new FixedTimeProvider(GeneratedAt));

        await Assert.ThrowsAsync<IOException>(() => handler.PrepareAsync(WorkItem()));

        builder.VerifyAll();
        exporter.VerifyAll();
        artifacts.VerifyAll();
        store.VerifyAll();
    }

    private static EmailDeliveryWorkItem WorkItem(
        EmailArtifactMetadata? attachment = null,
        string workbookExportedBy = "SYSTEM") =>
        new(
            71,
            901,
            17,
            "C1000001",
            "C1008267",
            "HYBRID",
            "FAKE",
            "SAFE_REDIRECT",
            2,
            GeneratedAt,
            workbookExportedBy,
            attachment);

    private static EmailArtifactMetadata Artifact(string fileName) =>
        new(
            fileName,
            Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "role-validation-application-artifacts",
                fileName)));

    private static OwnerScope Scope(
        string ownerEmployeeNo,
        IReadOnlyList<int> roleIds,
        IReadOnlyList<ApplicationUserView> rows,
        OwnerDeliverySeed? seed = null) =>
        new(
            17,
            "ERSN",
            "eRSN",
            ownerEmployeeNo,
            seed ?? OwnerDeliverySeed.Pending(ownerEmployeeNo),
            roleIds,
            rows);

    private static ApplicationUserView User(int roleId) =>
        new(
            "C2000001",
            "Example User",
            employeeStatus: null,
            sourceRoleKey: "SOURCE",
            sourceRoleDisplayName: "Source",
            roleId,
            roleName: "Role",
            SourceRoleResolutionType.Resolved);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
