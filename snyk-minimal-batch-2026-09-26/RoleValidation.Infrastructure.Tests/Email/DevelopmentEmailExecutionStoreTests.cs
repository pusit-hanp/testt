using System.Collections;
using System.Reflection;
using RoleValidation.Application.Administration;
using RoleValidation.Application.Email;
using RoleValidation.Application.History;
using RoleValidation.Core.Features.Applications;
using RoleValidation.Core.Features.Email;
using RoleValidation.Infrastructure.Development;
using RoleValidation.Infrastructure.Email;

namespace RoleValidation.Infrastructure.Tests.Email;

public sealed class DevelopmentEmailExecutionStoreTests
{
    private static readonly DateTimeOffset UtcNow = new(
        2026,
        8,
        30,
        5,
        0,
        0,
        TimeSpan.Zero);

    private static readonly EmailConfigurationSnapshot SafeSnapshot = new(
        "HYBRID",
        "FAKE",
        "SAFE_REDIRECT");

    [Fact]
    public async Task SaveScheduleAsync_Should_CreateInactiveCanonicalScheduleAndVisibleHistory()
    {
        (DevelopmentRoleVData data, DevelopmentEmailExecutionStore store) =
            CreateStore();
        IHistoryReader historyReader = data;
        DateTimeOffset supplied = Bangkok(2027, 3, 8, 9).AddTicks(17);
        DateTimeOffset canonical = supplied.AddTicks(-7);

        AdministrationResult result = await store.SaveScheduleAsync(
            new SaveEmailScheduleCommand(1, supplied, "C1008267"));

        Assert.True(result.Succeeded);
        EmailScheduleRow schedule = Assert.Single(await store.GetSchedulesAsync());
        Assert.Equal(result.EntityId, schedule.EmailScheduleId);
        Assert.Equal(1, schedule.ApplicationId);
        Assert.False(schedule.IsActive);
        Assert.Equal(canonical, schedule.NextRunAt);
        ChangeHistoryRow change = Assert.Single((await historyReader.ReadChangesAsync(
            new ChangeHistoryQuery(null, null, null))).Items);
        Assert.Equal("EmailSchedule", change.EntityType);
        Assert.Equal(result.EntityId!.Value.ToString(), change.EntityId);
        Assert.Equal("ScheduleChange", change.Action);
        ChangeHistoryDetail detail = Assert.IsType<ChangeHistoryDetail>(
            await historyReader.ReadChangeDetailAsync(
                change.ChangeHistoryId));
        Assert.Equal(change.ChangeHistoryId, detail.ChangeHistoryId);
        Assert.Equal(change.EntityType, detail.EntityType);
        Assert.Equal(change.EntityId, detail.EntityId);
        Assert.Equal(change.Action, detail.Action);
        Assert.Null(detail.OldValue);
        Assert.Equal(
            $"NextRunAt={canonical:O}; IsActive=false",
            detail.NewValue);
        Assert.Equal("C1008267", change.ChangedByEmployeeNo);
    }

    [Fact]
    public async Task SaveScheduleAsync_Should_UpsertByApplicationPreserveActiveAndSkipNormalizedNoOpHistory()
    {
        (DevelopmentRoleVData data, DevelopmentEmailExecutionStore store) =
            CreateStore();
        int scheduleId = await SaveAndActivateAsync(
            store,
            Bangkok(2027, 3, 8, 9));
        int historyBefore = data.ChangeHistory.Count;
        DateTimeOffset updatedAt = Bangkok(2028, 4, 9, 10);

        AdministrationResult update = await store.SaveScheduleAsync(
            new SaveEmailScheduleCommand(1, updatedAt, "C1008267"));
        AdministrationResult noOp = await store.SaveScheduleAsync(
            new SaveEmailScheduleCommand(
                1,
                updatedAt.AddTicks(9),
                "C1008267"));

        Assert.Equal(scheduleId, update.EntityId);
        Assert.Equal(scheduleId, noOp.EntityId);
        EmailScheduleRow row = Assert.Single(await store.GetSchedulesAsync());
        Assert.True(row.IsActive);
        Assert.Equal(updatedAt, row.NextRunAt);
        Assert.Equal(historyBefore + 1, data.ChangeHistory.Count);
    }

    [Fact]
    public async Task ActiveSchedule_Should_DeactivateAfterApplicationBecomesUnready()
    {
        (DevelopmentRoleVData data, DevelopmentEmailExecutionStore store) =
            CreateStore();
        int scheduleId = await SaveAndActivateAsync(
            store,
            Bangkok(2027, 3, 8, 9));
        int historyBeforeDeactivate = data.ChangeHistory.Count;
        DeactivateApplicationUnderSharedGate(data, 1);

        AdministrationResult activateWhileInactive =
            await store.SetScheduleActiveAsync(
                new SetEmailScheduleActiveCommand(
                    1,
                    scheduleId,
                    true,
                    "C1008267"));
        AdministrationResult deactivate = await store.SetScheduleActiveAsync(
            new SetEmailScheduleActiveCommand(
                1,
                scheduleId,
                false,
                "C1008267"));

        Assert.Equal("APPLICATION_INACTIVE", activateWhileInactive.ErrorCode);
        Assert.True(deactivate.Succeeded);
        Assert.False(Assert.Single(await store.GetSchedulesAsync()).IsActive);
        Assert.Equal(historyBeforeDeactivate + 1, data.ChangeHistory.Count);
    }

    [Fact]
    public async Task InactiveScheduleDeactivate_Should_BeTrueNoOpWithoutHistory()
    {
        (DevelopmentRoleVData data, DevelopmentEmailExecutionStore store) =
            CreateStore();
        AdministrationResult saved = await store.SaveScheduleAsync(
            new SaveEmailScheduleCommand(1, Bangkok(2027, 3, 8, 9), "C1008267"));
        int historyBefore = data.ChangeHistory.Count;

        AdministrationResult first = await store.SetScheduleActiveAsync(
            new SetEmailScheduleActiveCommand(
                1,
                saved.EntityId!.Value,
                false,
                "C1008267"));
        AdministrationResult second = await store.SetScheduleActiveAsync(
            new SetEmailScheduleActiveCommand(
                1,
                saved.EntityId.Value,
                false,
                "C1008267"));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(historyBefore, data.ChangeHistory.Count);
    }

    [Fact]
    public async Task ScheduleMutation_Should_AllowApprovedAndRejectCrossApplicationTargetsWithoutMutation()
    {
        (DevelopmentRoleVData data, DevelopmentEmailExecutionStore store) =
            CreateStore();
        AdministrationResult otherApplication = await store.SaveScheduleAsync(
            new SaveEmailScheduleCommand(2, Bangkok(2027, 3, 8, 9), "C1008267"));
        AdministrationResult saved = await store.SaveScheduleAsync(
            new SaveEmailScheduleCommand(1, Bangkok(2027, 3, 8, 9), "C1008267"));

        AdministrationResult forged = await store.SetScheduleActiveAsync(
            new SetEmailScheduleActiveCommand(
                2,
                saved.EntityId!.Value,
                false,
                "C1008267"));

        Assert.True(otherApplication.Succeeded);
        Assert.Equal("EMAIL_SCHEDULE_NOT_FOUND", forged.ErrorCode);
        Assert.Equal(2, (await store.GetSchedulesAsync()).Count);
        Assert.Equal(2, data.ChangeHistory.Count);
    }

    [Fact]
    public async Task ScheduleSave_Should_RejectMissingAndInactiveApplications()
    {
        (DevelopmentRoleVData data, DevelopmentEmailExecutionStore store) =
            CreateStore();
        DeactivateApplicationUnderSharedGate(data, 1);

        AdministrationResult missing = await store.SaveScheduleAsync(
            new SaveEmailScheduleCommand(999, Bangkok(2027, 3, 8, 9), "C1008267"));
        AdministrationResult inactive = await store.SaveScheduleAsync(
            new SaveEmailScheduleCommand(1, Bangkok(2027, 3, 8, 9), "C1008267"));

        Assert.Equal("APPLICATION_NOT_FOUND", missing.ErrorCode);
        Assert.Equal("APPLICATION_INACTIVE", inactive.ErrorCode);
        Assert.Empty(await store.GetSchedulesAsync());
        Assert.Empty(data.ChangeHistory);
    }

    [Fact]
    public async Task ConcurrentScheduleUpdates_Should_LeaveMatchingMutationAndHistoryCounts()
    {
        (DevelopmentRoleVData data, DevelopmentEmailExecutionStore store) =
            CreateStore();
        await store.SaveScheduleAsync(new SaveEmailScheduleCommand(
            1,
            Bangkok(2027, 3, 8, 9),
            "C1008267"));

        Task<AdministrationResult>[] updates =
        [
            Task.Run(() => store.SaveScheduleAsync(new SaveEmailScheduleCommand(
                1,
                Bangkok(2028, 4, 9, 10),
                "C1008267"))),
            Task.Run(() => store.SaveScheduleAsync(new SaveEmailScheduleCommand(
                1,
                Bangkok(2029, 5, 10, 11),
                "C1008267")))
        ];
        await Task.WhenAll(updates);

        Assert.All(updates, task => Assert.True(task.Result.Succeeded));
        Assert.Single(await store.GetSchedulesAsync());
        Assert.Equal(3, data.ChangeHistory.Count);
        ChangeHistoryPage visible = await ((IHistoryReader)data).ReadChangesAsync(
            new ChangeHistoryQuery(null, "EmailSchedule", "ScheduleChange"));
        Assert.Equal(3, visible.TotalCount);
    }

    [Fact]
    public async Task ConcurrentScheduledCreation_Should_HaveOneWinnerAndOneAnnualAdvance()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        DateTimeOffset due = Bangkok(2026, 8, 30, 9);
        await SaveAndActivateAsync(store, due);
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<CreateEmailRunResult>[] calls = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(async () =>
            {
                await start.Task;
                return await store.TryCreateScheduledRunAsync(
                    Bangkok(2026, 8, 30, 10),
                    SafeSnapshot);
            }))
            .ToArray();

        start.SetResult();
        CreateEmailRunResult[] results = await Task.WhenAll(calls);

        Assert.Single(results, result =>
            result.Outcome == EmailRunCreationOutcome.Created);
        Assert.Equal(19, results.Count(result =>
            result.Outcome == EmailRunCreationOutcome.NoDueSchedule));
        Assert.Single(await store.GetPendingRunsAsync(20));
        Assert.Single(ReadInternal(store, "Runs"));
        Assert.Equal(
            Bangkok(2027, 8, 30, 9),
            Assert.Single(await store.GetSchedulesAsync()).NextRunAt);
    }

    [Fact]
    public async Task ScheduledCreation_Should_IgnoreDueScheduleWhenApplicationBecomesInactive()
    {
        (DevelopmentRoleVData data, DevelopmentEmailExecutionStore store) =
            CreateStore();
        DateTimeOffset due = Bangkok(2026, 8, 30, 9);
        int scheduleId = await SaveAndActivateAsync(store, due);
        DeactivateApplicationUnderSharedGate(data, 1);

        CreateEmailRunResult result = await store.TryCreateScheduledRunAsync(
            Bangkok(2026, 8, 30, 10),
            SafeSnapshot);

        Assert.Equal(EmailRunCreationOutcome.NoDueSchedule, result.Outcome);
        Assert.Empty(ReadInternal(store, "Runs"));
        EmailScheduleRow unchanged = Assert.Single(
            await store.GetSchedulesAsync());
        Assert.True(unchanged.IsActive);
        Assert.Equal(due, unchanged.NextRunAt);

        AdministrationResult deactivated = await store.SetScheduleActiveAsync(
            new SetEmailScheduleActiveCommand(
                1,
                scheduleId,
                false,
                "C1008267"));

        Assert.True(deactivated.Succeeded);
        Assert.False(Assert.Single(await store.GetSchedulesAsync()).IsActive);
    }

    [Fact]
    public async Task ScheduledCreation_Should_CreateOnlyLatestMissedOccurrenceBeforeCurrentYearDate()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await SaveAndActivateAsync(store, Bangkok(2023, 12, 1, 9));

        CreateEmailRunResult result = await store.TryCreateScheduledRunAsync(
            Bangkok(2026, 8, 30, 12),
            SafeSnapshot);

        Assert.Equal(EmailRunCreationOutcome.Created, result.Outcome);
        Assert.Equal(Bangkok(2025, 12, 1, 9), result.ScheduledFor);
        Assert.Equal(
            Bangkok(2026, 12, 1, 9),
            Assert.Single(await store.GetSchedulesAsync()).NextRunAt);
        Assert.Single(await store.GetPendingRunsAsync(10));
    }

    [Fact]
    public async Task ScheduledCreation_Should_UseCurrentYearOccurrenceAfterAnnualDate()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await SaveAndActivateAsync(store, Bangkok(2023, 3, 1, 9));

        CreateEmailRunResult result = await store.TryCreateScheduledRunAsync(
            Bangkok(2026, 8, 30, 12),
            SafeSnapshot);

        Assert.Equal(Bangkok(2026, 3, 1, 9), result.ScheduledFor);
        Assert.Equal(
            Bangkok(2027, 3, 1, 9),
            Assert.Single(await store.GetSchedulesAsync()).NextRunAt);
    }

    [Fact]
    public async Task ScheduledCreation_Should_CanonicalizeOccurrenceForComparisonAndIdempotency()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        DateTimeOffset supplied = Bangkok(2026, 8, 30, 9).AddTicks(7);
        await SaveAndActivateAsync(store, supplied);

        CreateEmailRunResult created = await store.TryCreateScheduledRunAsync(
            Bangkok(2026, 8, 30, 10),
            SafeSnapshot);
        object firstRun = SingleInternal(store, "Runs");
        string firstKey = GetInternal<string>(firstRun, "IdempotencyKey");
        SetInternalProperty(firstRun, "Status", EmailRunStatus.Completed);
        await store.SaveScheduleAsync(new SaveEmailScheduleCommand(
            1,
            supplied.AddTicks(2),
            "C1008267"));
        CreateEmailRunResult repeated = await store.TryCreateScheduledRunAsync(
            Bangkok(2026, 8, 30, 10),
            SafeSnapshot);
        CreateEmailRunResult nextYear = await store.TryCreateScheduledRunAsync(
            Bangkok(2027, 8, 30, 10),
            SafeSnapshot);

        DateTimeOffset canonical = supplied.AddTicks(-7);
        Assert.Equal(canonical, created.ScheduledFor);
        Assert.Equal(
            $"SCHEDULED:1:{created.EmailScheduleId}:{canonical:O}",
            firstKey);
        Assert.Equal(EmailRunCreationOutcome.OccurrenceAlreadyRecorded, repeated.Outcome);
        Assert.Equal(EmailRunCreationOutcome.Created, nextYear.Outcome);
        object[] runs = ReadInternal(store, "Runs").ToArray();
        Assert.Equal(2, runs.Length);
        string secondKey = GetInternal<string>(runs[1], "IdempotencyKey");
        Assert.Equal(
            $"SCHEDULED:1:{created.EmailScheduleId}:{Bangkok(2027, 8, 30, 9):O}",
            secondKey);
        Assert.NotEqual(firstKey, secondKey);
        Assert.DoesNotContain("0000007", firstKey, StringComparison.Ordinal);
        Assert.DoesNotContain("0000009", secondKey, StringComparison.Ordinal);
        Assert.Equal(
            Bangkok(2028, 8, 30, 9),
            Assert.Single(await store.GetSchedulesAsync()).NextRunAt);
    }

    [Theory]
    [InlineData(EmailRunStatus.Pending)]
    [InlineData(EmailRunStatus.Processing)]
    [InlineData(EmailRunStatus.ReviewRequired)]
    public async Task ScheduledCreation_Should_NotAdvanceWhenAnyActiveRunExists(
        EmailRunStatus activeStatus)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        DateTimeOffset due = Bangkok(2026, 8, 30, 9);
        await SaveAndActivateAsync(store, due);
        await store.TryCreateRunNowAsync(1, "C1008267", SafeSnapshot);
        SetInternalProperty(SingleInternal(store, "Runs"), "Status", activeStatus);

        CreateEmailRunResult result = await store.TryCreateScheduledRunAsync(
            Bangkok(2026, 8, 30, 10),
            SafeSnapshot);

        Assert.Equal(EmailRunCreationOutcome.BlockedByActiveRun, result.Outcome);
        Assert.Equal("EMAIL_ACTIVE_RUN_EXISTS", result.ErrorCode);
        Assert.Equal(due, Assert.Single(await store.GetSchedulesAsync()).NextRunAt);
    }

    [Fact]
    public async Task ScheduledCreation_Should_SkipBlockedEarlierApplicationAndCreateLaterDueRun()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        DateTimeOffset due = Bangkok(2026, 8, 30, 9);
        int blockedScheduleId = await SaveAndActivateAsync(
            store,
            due,
            applicationId: 1);
        int eligibleScheduleId = await SaveAndActivateAsync(
            store,
            due,
            applicationId: 2);
        CreateEmailRunResult activeRun = await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot);
        Assert.Equal(EmailRunCreationOutcome.Created, activeRun.Outcome);

        CreateEmailRunResult result = await store.TryCreateScheduledRunAsync(
            Bangkok(2026, 8, 30, 10),
            SafeSnapshot);

        Assert.Equal(EmailRunCreationOutcome.Created, result.Outcome);
        Assert.Equal(2, result.ApplicationId);
        Assert.Equal(eligibleScheduleId, result.EmailScheduleId);
        Assert.Equal(due, result.ScheduledFor);
        EmailScheduleRow[] schedules = (await store.GetSchedulesAsync())
            .OrderBy(schedule => schedule.EmailScheduleId)
            .ToArray();
        Assert.Equal(blockedScheduleId, schedules[0].EmailScheduleId);
        Assert.Equal(due, schedules[0].NextRunAt);
        Assert.Equal(eligibleScheduleId, schedules[1].EmailScheduleId);
        Assert.Equal(Bangkok(2027, 8, 30, 9), schedules[1].NextRunAt);
    }

    [Theory]
    [InlineData(EmailRunStatus.Pending)]
    [InlineData(EmailRunStatus.Processing)]
    [InlineData(EmailRunStatus.ReviewRequired)]
    public async Task RunNow_Should_BeBlockedByEveryActiveRunStatus(
        EmailRunStatus activeStatus)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await store.TryCreateRunNowAsync(1, "C1008267", SafeSnapshot);
        SetInternalProperty(SingleInternal(store, "Runs"), "Status", activeStatus);

        CreateEmailRunResult result = await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot);

        Assert.Equal(EmailRunCreationOutcome.BlockedByActiveRun, result.Outcome);
        Assert.Equal("EMAIL_ACTIVE_RUN_EXISTS", result.ErrorCode);
        Assert.Single(ReadInternal(store, "Runs"));
    }

    [Fact]
    public async Task RunNow_Should_WorkWithoutScheduleAndNeverChangeExistingSchedule()
    {
        (_, DevelopmentEmailExecutionStore noScheduleStore) = CreateStore();

        CreateEmailRunResult withoutSchedule =
            await noScheduleStore.TryCreateRunNowAsync(
                1,
                "C1008267",
                SafeSnapshot);

        Assert.Equal(EmailRunCreationOutcome.Created, withoutSchedule.Outcome);
        Assert.Null(withoutSchedule.EmailScheduleId);
        Assert.Null(withoutSchedule.ScheduledFor);
        Assert.Empty(await noScheduleStore.GetSchedulesAsync());

        (_, DevelopmentEmailExecutionStore scheduledStore) = CreateStore();
        DateTimeOffset nextRunAt = Bangkok(2027, 3, 8, 9);
        await scheduledStore.SaveScheduleAsync(new SaveEmailScheduleCommand(
            1,
            nextRunAt,
            "C1008267"));

        CreateEmailRunResult withInactiveSchedule =
            await scheduledStore.TryCreateRunNowAsync(
                1,
                "C1008267",
                SafeSnapshot);

        Assert.Equal(EmailRunCreationOutcome.Created, withInactiveSchedule.Outcome);
        Assert.Equal(
            nextRunAt,
            Assert.Single(await scheduledStore.GetSchedulesAsync()).NextRunAt);
    }

    [Fact]
    public async Task RunNow_Should_GenerateDistinctSystemKeysWithoutRequestKeyInput()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();

        CreateEmailRunResult first = await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot);
        SetInternalProperty(
            SingleInternal(store, "Runs"),
            "Status",
            EmailRunStatus.Completed);
        CreateEmailRunResult second = await store.TryCreateRunNowAsync(
            1,
            " 00090775 ",
            SafeSnapshot);

        Assert.Equal(EmailRunCreationOutcome.Created, first.Outcome);
        Assert.Equal(EmailRunCreationOutcome.Created, second.Outcome);
        string[] keys = ReadInternal(store, "Runs")
            .Select(run => GetInternal<string>(run, "IdempotencyKey"))
            .ToArray();
        Assert.Equal(2, keys.Length);
        Assert.All(keys, key => Assert.StartsWith("RUN_NOW:1:", key));
        Assert.NotEqual(keys[0], keys[1]);
        Assert.DoesNotContain("C1008267", keys[0], StringComparison.Ordinal);
        Assert.DoesNotContain("00090775", keys[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunCreation_Should_PersistUppercaseSnapshotSuppliedByCaller()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        var snapshot = new EmailConfigurationSnapshot(
            " oracle ",
            "api_email",
            "safe_redirect");

        await store.TryCreateRunNowAsync(1, " C1008267 ", snapshot);
        PendingEmailRun run = Assert.Single(await store.GetPendingRunsAsync(10));

        Assert.Equal("ORACLE", run.Configuration.DataSource);
        Assert.Equal("API_EMAIL", run.Configuration.TransportMode);
        Assert.Equal("SAFE_REDIRECT", run.Configuration.RecipientMode);
        Assert.Equal("C1008267", run.TriggeredByEmployeeNo);
    }

    [Fact]
    public async Task TryFailPendingRunPreparationAsync_Should_FinalizeOnlyPendingRunWithoutDeliveries()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = (await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot)).EmailRunId!.Value;

        bool applied = await store.TryFailPendingRunPreparationAsync(runId);
        bool repeated = await store.TryFailPendingRunPreparationAsync(runId);

        Assert.True(applied);
        Assert.False(repeated);
        Assert.Empty(await store.GetPendingRunsAsync(10));
        object run = Assert.Single(ReadInternal(store, "Runs"));
        Assert.Equal(
            EmailRunStatus.Failed,
            GetInternal<EmailRunStatus>(run, "Status"));
        Assert.Null(GetInternal<DateTimeOffset?>(run, "StartedAt"));
        Assert.Equal(
            new DateTimeOffset(2026, 8, 30, 5, 0, 0, TimeSpan.Zero),
            GetInternal<DateTimeOffset?>(run, "CompletedAt"));
        Assert.Equal(
            "EMAIL_RUN_PREPARATION_FAILED",
            GetInternal<string?>(run, "LastErrorCode"));
        Assert.Null(GetInternal<string?>(run, "LastErrorMessage"));
        Assert.Empty(ReadInternal(store, "Deliveries"));
    }

    [Fact]
    public async Task ScheduledPendingRun_Should_HaveNoTriggeringEmployee()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await SaveAndActivateAsync(store, Bangkok(2026, 8, 30, 9));

        await store.TryCreateScheduledRunAsync(
            Bangkok(2026, 8, 30, 10),
            SafeSnapshot);

        Assert.Null(Assert.Single(
            await store.GetPendingRunsAsync(10)).TriggeredByEmployeeNo);
    }

    [Fact]
    public async Task RunNow_Should_RecheckApplicationReadiness()
    {
        (DevelopmentRoleVData data, DevelopmentEmailExecutionStore store) =
            CreateStore();

        CreateEmailRunResult approved = await store.TryCreateRunNowAsync(
            2,
            "C1008267",
            SafeSnapshot);
        DeactivateApplicationUnderSharedGate(data, 1);
        CreateEmailRunResult inactive = await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot);
        CreateEmailRunResult missing = await store.TryCreateRunNowAsync(
            999,
            "C1008267",
            SafeSnapshot);

        Assert.Equal(EmailRunCreationOutcome.Created, approved.Outcome);
        Assert.Equal("APPLICATION_INACTIVE", inactive.ErrorCode);
        Assert.Equal("APPLICATION_NOT_FOUND", missing.ErrorCode);
        Assert.Single(ReadInternal(store, "Runs"));
    }

    [Fact]
    public async Task TryStartRunAsync_Should_RejectDuplicateOwnerSeedsWithoutPartialMutation()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = (await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot)).EmailRunId!.Value;

        await Assert.ThrowsAsync<ArgumentException>(() => store.TryStartRunAsync(
            runId,
            [
                OwnerDeliverySeed.Pending("00090775"),
                OwnerDeliverySeed.Pending(" 00090775 ")
            ]));

        Assert.Single(await store.GetPendingRunsAsync(10));
        Assert.Empty(ReadInternal(store, "Deliveries"));
        Assert.Equal(EmailRunStatus.Pending, GetInternal<EmailRunStatus>(
            SingleInternal(store, "Runs"),
            "Status"));
    }

    [Fact]
    public async Task TryStartRunAsync_Should_InsertCompleteDistinctSeedsAndDeriveEffectiveEmployees()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = (await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot)).EmailRunId!.Value;

        StartEmailRunResult result = await store.TryStartRunAsync(
            runId,
            [
                OwnerDeliverySeed.Pending("00090775"),
                OwnerDeliverySeed.Failed("00090622", "OWNER_INACTIVE")
            ]);

        Assert.Equal(StartEmailRunOutcome.Started, result.Outcome);
        Assert.Equal(EmailRunStatus.Processing, result.RunStatus);
        object[] deliveries = ReadInternal(store, "Deliveries").ToArray();
        Assert.Equal(2, deliveries.Length);
        Assert.All(deliveries, delivery => Assert.Equal(
            "C2001234",
            GetInternal<string>(delivery, "EffectiveEmployeeNo")));
        Assert.Equal(
            EmailDeliveryStatus.Failed,
            GetInternal<EmailDeliveryStatus>(deliveries[1], "Status"));
        Assert.Equal("OWNER_INACTIVE", GetInternal<string?>(
            deliveries[1],
            "LastErrorCode"));
        object run = SingleInternal(store, "Runs");
        Assert.Null(GetInternal<DateTimeOffset?>(run, "CompletedAt"));
        Assert.Null(GetInternal<string?>(run, "LastErrorCode"));
    }

    [Fact]
    public async Task TryStartRunAsync_Should_InsertEveryFailedSeedAndFinalizeRunAtomically()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = (await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot)).EmailRunId!.Value;

        StartEmailRunResult result = await store.TryStartRunAsync(
            runId,
            [
                OwnerDeliverySeed.Failed(
                    "00090775",
                    "OWNER_INACTIVE"),
                OwnerDeliverySeed.Failed(
                    "00090622",
                    "OWNER_EMPLOYEE_NOT_FOUND")
            ]);

        Assert.Equal(StartEmailRunOutcome.Started, result.Outcome);
        Assert.Equal(EmailRunStatus.Failed, result.RunStatus);
        Assert.Null(result.ErrorCode);

        object[] deliveries = ReadInternal(store, "Deliveries").ToArray();
        Assert.Equal(2, deliveries.Length);
        Assert.All(deliveries, delivery => Assert.Equal(
            EmailDeliveryStatus.Failed,
            GetInternal<EmailDeliveryStatus>(delivery, "Status")));
        Assert.Equal(
            ["OWNER_INACTIVE", "OWNER_EMPLOYEE_NOT_FOUND"],
            deliveries.Select(delivery =>
                GetInternal<string?>(delivery, "LastErrorCode")));

        object run = SingleInternal(store, "Runs");
        DateTimeOffset? startedAt = GetInternal<DateTimeOffset?>(
            run,
            "StartedAt");
        Assert.NotNull(startedAt);
        Assert.Equal(startedAt, GetInternal<DateTimeOffset?>(
            run,
            "CompletedAt"));
        Assert.Equal(EmailRunStatus.Failed, GetInternal<EmailRunStatus>(
            run,
            "Status"));
        Assert.Null(GetInternal<string?>(run, "LastErrorCode"));
        Assert.Empty(await store.GetPendingRunsAsync(10));
        Assert.Null(await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)));
    }

    [Fact]
    public async Task TryStartRunAsync_Should_DeriveRoleOwnerEffectiveEmployeeFromRunSnapshot()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        var snapshot = new EmailConfigurationSnapshot(
            "ORACLE",
            "API_EMAIL",
            "ROLE_OWNER");
        long runId = (await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            snapshot)).EmailRunId!.Value;

        await store.TryStartRunAsync(
            runId,
            [OwnerDeliverySeed.Pending("00090775")]);

        object delivery = SingleInternal(store, "Deliveries");
        Assert.Equal("00090775", GetInternal<string>(
            delivery,
            "EffectiveEmployeeNo"));
        Assert.Equal("ROLE_OWNER", GetInternal<string>(delivery, "RecipientMode"));
        Assert.Equal("API_EMAIL", GetInternal<string>(delivery, "TransportMode"));
    }

    [Fact]
    public async Task TryStartRunAsync_Should_FinalizeEmptySeedRunInSameClaim()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = (await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot)).EmailRunId!.Value;

        StartEmailRunResult result = await store.TryStartRunAsync(runId, []);

        Assert.Equal(StartEmailRunOutcome.FailedNoEligibleDelivery, result.Outcome);
        Assert.Equal("NO_ELIGIBLE_DELIVERY", result.ErrorCode);
        object run = SingleInternal(store, "Runs");
        Assert.Equal(EmailRunStatus.Failed, GetInternal<EmailRunStatus>(run, "Status"));
        Assert.NotNull(GetInternal<DateTimeOffset?>(run, "StartedAt"));
        Assert.NotNull(GetInternal<DateTimeOffset?>(run, "CompletedAt"));
        Assert.Equal("NO_ELIGIBLE_DELIVERY", GetInternal<string?>(
            run,
            "LastErrorCode"));
        Assert.Empty(await store.GetPendingRunsAsync(10));
    }

    [Fact]
    public async Task TryStartRunAsync_Should_ReturnTypedStaleLoser()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = (await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot)).EmailRunId!.Value;
        await store.TryStartRunAsync(
            runId,
            [OwnerDeliverySeed.Pending("00090775")]);

        StartEmailRunResult stale = await store.TryStartRunAsync(
            runId,
            [OwnerDeliverySeed.Pending("00090622")]);
        StartEmailRunResult missing = await store.TryStartRunAsync(999, []);

        Assert.Equal(StartEmailRunOutcome.Rejected, stale.Outcome);
        Assert.Equal("EMAIL_RUN_NOT_PENDING", stale.ErrorCode);
        Assert.Equal("EMAIL_RUN_NOT_FOUND", missing.ErrorCode);
        Assert.Single(ReadInternal(store, "Deliveries"));
    }

    [Fact]
    public async Task ConcurrentTryStartRunAsync_Should_HaveOneAtomicWinner()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = (await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot)).EmailRunId!.Value;
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        OwnerDeliverySeed[][] callerSeeds = Enumerable.Range(0, 20)
            .Select(index => new[]
            {
                OwnerDeliverySeed.Pending(index.ToString("D8")),
                OwnerDeliverySeed.Pending((index + 100).ToString("D8"))
            })
            .ToArray();
        Task<StartEmailRunResult>[] calls = callerSeeds
            .Select(seeds => Task.Run(async () =>
            {
                await start.Task;
                return await store.TryStartRunAsync(runId, seeds);
            }))
            .ToArray();

        start.SetResult();
        StartEmailRunResult[] results = await Task.WhenAll(calls);

        int winnerIndex = Array.FindIndex(results, result =>
            result.Outcome == StartEmailRunOutcome.Started);
        Assert.InRange(winnerIndex, 0, 19);
        Assert.Single(results, result =>
            result.Outcome == StartEmailRunOutcome.Started);
        Assert.Equal(19, results.Count(result =>
            result.Outcome == StartEmailRunOutcome.Rejected &&
            result.ErrorCode == "EMAIL_RUN_NOT_PENDING"));
        string[] expectedOwners = callerSeeds[winnerIndex]
            .Select(seed => seed.OwnerEmployeeNo)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] actualOwners = ReadInternal(store, "Deliveries")
            .Select(delivery => GetInternal<string>(delivery, "OwnerEmployeeNo"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedOwners, actualOwners);
        object run = SingleInternal(store, "Runs");
        Assert.Equal(
            EmailRunStatus.Processing,
            GetInternal<EmailRunStatus>(run, "Status"));
        Assert.NotNull(GetInternal<DateTimeOffset?>(run, "StartedAt"));
    }

    [Fact]
    public async Task TryClaimNextDeliveryAsync_Should_ClaimPendingAndSetAttemptMetadata()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        DateTimeOffset now = Bangkok(2026, 8, 30, 12).AddTicks(19);
        DateTimeOffset expected = now.ToUniversalTime().AddTicks(-9);
        var expectedWorkItem = new EmailDeliveryWorkItem(
            1,
            runId,
            1,
            "00090775",
            "C2001234",
            "HYBRID",
            "FAKE",
            "SAFE_REDIRECT",
            1,
            expected,
            "C1008267");

        EmailDeliveryWorkItem? work = await store.TryClaimNextDeliveryAsync(now);

        Assert.Equal(expectedWorkItem, work);
        object delivery = SingleInternal(store, "Deliveries");
        Assert.Equal(EmailDeliveryStatus.Preparing, GetInternal<EmailDeliveryStatus>(
            delivery,
            "Status"));
        Assert.Null(GetInternal<DateTimeOffset?>(delivery, "NextRetryAt"));
    }

    [Fact]
    public async Task TryClaimNextDeliveryAsync_Should_NotMutateWhenDetachedWorkItemConstructionOverflows()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        SetInternalProperty(delivery, "AttemptCount", int.MaxValue);

        await Assert.ThrowsAsync<OverflowException>(() =>
            store.TryClaimNextDeliveryAsync(Bangkok(2026, 8, 30, 12)));

        Assert.Equal(
            EmailDeliveryStatus.Pending,
            GetInternal<EmailDeliveryStatus>(delivery, "Status"));
        Assert.Equal(int.MaxValue, GetInternal<int>(delivery, "AttemptCount"));
        Assert.Null(GetInternal<DateTimeOffset?>(delivery, "LastAttemptAt"));
    }

    [Fact]
    public async Task TryClaimNextDeliveryAsync_Should_ProjectSystemForScheduledRun()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        DateTimeOffset due = Bangkok(2026, 8, 30, 9);
        await SaveAndActivateAsync(store, due);
        CreateEmailRunResult created = await store.TryCreateScheduledRunAsync(
            Bangkok(2026, 8, 30, 10),
            SafeSnapshot);
        await store.TryStartRunAsync(
            created.EmailRunId!.Value,
            [OwnerDeliverySeed.Pending("00090775")]);

        EmailDeliveryWorkItem work = (await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;

        Assert.Equal("SYSTEM", work.WorkbookExportedBy);
    }

    [Fact]
    public async Task TryClaimNextDeliveryAsync_Should_TrimPaddedRunNowActor()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object run = SingleInternal(store, "Runs");
        SetInternalBackingField(run, "TriggerType", "RUN_NOW");
        SetInternalBackingField(run, "TriggeredByEmployeeNo", "  C1008267  ");

        EmailDeliveryWorkItem work = (await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;

        Assert.Equal("C1008267", work.WorkbookExportedBy);
    }

    [Theory]
    [InlineData("SCHEDULED", "C1008267")]
    [InlineData("SCHEDULED", " ")]
    [InlineData("SCHEDULED", "\t")]
    [InlineData("RUN_NOW", null)]
    [InlineData("UNKNOWN", null)]
    public async Task TryClaimNextDeliveryAsync_Should_RejectInvalidPersistedActorBeforeMutation(
        string triggerType,
        string? triggeredByEmployeeNo)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object run = SingleInternal(store, "Runs");
        SetInternalBackingField(run, "TriggerType", triggerType);
        SetInternalBackingField(run, "TriggeredByEmployeeNo", triggeredByEmployeeNo);
        object delivery = SingleInternal(store, "Deliveries");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.TryClaimNextDeliveryAsync(Bangkok(2026, 8, 30, 12)));

        Assert.Equal(
            EmailDeliveryStatus.Pending,
            GetInternal<EmailDeliveryStatus>(delivery, "Status"));
        Assert.Equal(0, GetInternal<int>(delivery, "AttemptCount"));
        Assert.Null(GetInternal<DateTimeOffset?>(delivery, "LastAttemptAt"));
    }

    [Fact]
    public async Task TryClaimNextDeliveryAsync_Should_ClaimRetryAtExactBoundaryAndIgnoreNullOrFuture()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"),
            OwnerDeliverySeed.Pending("00090622"),
            OwnerDeliverySeed.Pending("62010967"));
        object[] deliveries = ReadInternal(store, "Deliveries").ToArray();
        DateTimeOffset now = Bangkok(2026, 8, 30, 12);
        SetInternalProperty(deliveries[0], "Status", EmailDeliveryStatus.RetryWait);
        SetInternalProperty(deliveries[0], "NextRetryAt", now);
        SetInternalProperty(deliveries[1], "Status", EmailDeliveryStatus.RetryWait);
        SetInternalProperty(deliveries[1], "NextRetryAt", null);
        SetInternalProperty(deliveries[2], "Status", EmailDeliveryStatus.RetryWait);
        SetInternalProperty(deliveries[2], "NextRetryAt", now.AddTicks(1));

        EmailDeliveryWorkItem? due = await store.TryClaimNextDeliveryAsync(now);
        EmailDeliveryWorkItem? none = await store.TryClaimNextDeliveryAsync(now);

        Assert.Equal("00090775", due!.OwnerEmployeeNo);
        Assert.Equal(1, due.AttemptCount);
        Assert.Equal(now.ToUniversalTime(), due.LastAttemptAt);
        Assert.Equal(
            EmailDeliveryStatus.Preparing,
            GetInternal<EmailDeliveryStatus>(deliveries[0], "Status"));
        Assert.Null(GetInternal<DateTimeOffset?>(deliveries[0], "NextRetryAt"));
        Assert.Null(none);
        Assert.All(deliveries[1..], delivery =>
        {
            Assert.Equal(
                EmailDeliveryStatus.RetryWait,
                GetInternal<EmailDeliveryStatus>(delivery, "Status"));
            Assert.Equal(0, GetInternal<int>(delivery, "AttemptCount"));
            Assert.Null(GetInternal<DateTimeOffset?>(delivery, "LastAttemptAt"));
        });
    }

    [Theory]
    [InlineData(EmailRunStatus.Pending)]
    [InlineData(EmailRunStatus.ReviewRequired)]
    [InlineData(EmailRunStatus.Completed)]
    [InlineData(EmailRunStatus.Partial)]
    [InlineData(EmailRunStatus.Failed)]
    [InlineData(EmailRunStatus.Cancelled)]
    public async Task TryClaimNextDeliveryAsync_Should_RequireProcessingRun(
        EmailRunStatus nonProcessingStatus)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        SetInternalProperty(
            SingleInternal(store, "Runs"),
            "Status",
            nonProcessingStatus);

        EmailDeliveryWorkItem? work = await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12));

        Assert.Null(work);
        Assert.Equal(
            EmailDeliveryStatus.Pending,
            GetInternal<EmailDeliveryStatus>(
                SingleInternal(store, "Deliveries"),
                "Status"));
        Assert.Equal(
            0,
            GetInternal<int>(
                SingleInternal(store, "Deliveries"),
                "AttemptCount"));
        Assert.Null(GetInternal<DateTimeOffset?>(
            SingleInternal(store, "Deliveries"),
            "LastAttemptAt"));
        Assert.Null(GetInternal<DateTimeOffset?>(
            SingleInternal(store, "Deliveries"),
            "NextRetryAt"));
    }

    [Fact]
    public async Task ConcurrentClaims_Should_HaveOneWinnerAndReturnDetachedWork()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        DateTimeOffset now = Bangkok(2026, 8, 30, 12);
        Task<EmailDeliveryWorkItem?>[] claims = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => store.TryClaimNextDeliveryAsync(now)))
            .ToArray();

        await Task.WhenAll(claims);

        EmailDeliveryWorkItem winner = Assert.Single(
            claims.Where(task => task.Result is not null)
                .Select(task => task.Result!));
        Assert.Equal(1, winner.AttemptCount);
        Assert.Equal(
            1,
            GetInternal<int>(SingleInternal(store, "Deliveries"), "AttemptCount"));
    }

    [Fact]
    public async Task TryClaimNextDeliveryAsync_Should_ReturnCompleteAttachmentAndFailClosedOnPartialPair()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        EmailArtifactMetadata artifact = Artifact("owner-1.xlsx");
        SetStoredArtifact(delivery, artifact);

        EmailDeliveryWorkItem? work = await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12));

        Assert.NotNull(work);
        Assert.Equal(artifact, work.Attachment);
        Assert.NotSame(artifact, work.Attachment);

        (_, DevelopmentEmailExecutionStore partialStore) = CreateStore();
        await CreateProcessingRunAsync(
            partialStore,
            OwnerDeliverySeed.Pending("00090775"));
        object partial = SingleInternal(partialStore, "Deliveries");
        SetInternalProperty(partial, "AttachmentFileName", "owner-1.xlsx");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            partialStore.TryClaimNextDeliveryAsync(
                Bangkok(2026, 8, 30, 12)));
        Assert.Equal(
            EmailDeliveryStatus.Pending,
            GetInternal<EmailDeliveryStatus>(partial, "Status"));
        Assert.Equal(0, GetInternal<int>(partial, "AttemptCount"));
        Assert.Null(GetInternal<DateTimeOffset?>(partial, "LastAttemptAt"));
    }

    [Fact]
    public async Task TryPersistDeliveryArtifactAsync_Should_ApplyReplayAndRejectEveryLeaseMismatchWithoutOverwrite()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        EmailDeliveryWorkItem work = (await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;
        EmailArtifactMetadata artifact = Artifact("owner-1.xlsx");
        EmailArtifactMetadata different = Artifact("other.xlsx");

        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Applied,
            await store.TryPersistDeliveryArtifactAsync(
                work.AttemptLease,
                artifact));
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.AlreadyMatched,
            await store.TryPersistDeliveryArtifactAsync(
                work.AttemptLease,
                artifact));
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistDeliveryArtifactAsync(
                new EmailDeliveryAttemptLease(
                    work.EmailDeliveryId + 999,
                    work.EmailRunId,
                    work.AttemptCount,
                    work.LastAttemptAt),
                artifact));
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistDeliveryArtifactAsync(
                new EmailDeliveryAttemptLease(
                    work.EmailDeliveryId,
                    work.EmailRunId + 1,
                    work.AttemptCount,
                    work.LastAttemptAt),
                artifact));
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistDeliveryArtifactAsync(
                new EmailDeliveryAttemptLease(
                    work.EmailDeliveryId,
                    work.EmailRunId,
                    work.AttemptCount + 1,
                    work.LastAttemptAt),
                artifact));
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistDeliveryArtifactAsync(
                new EmailDeliveryAttemptLease(
                    work.EmailDeliveryId,
                    work.EmailRunId,
                    work.AttemptCount,
                    work.LastAttemptAt.AddTicks(10)),
                artifact));
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistDeliveryArtifactAsync(
                work.AttemptLease,
                different));

        object delivery = SingleInternal(store, "Deliveries");
        SetInternalProperty(delivery, "Status", EmailDeliveryStatus.Pending);
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistDeliveryArtifactAsync(
                work.AttemptLease,
                artifact));
        SetInternalProperty(delivery, "Status", EmailDeliveryStatus.RetryWait);
        SetInternalProperty(delivery, "NextRetryAt", work.LastAttemptAt);
        EmailDeliveryWorkItem secondAttempt =
            (await store.TryClaimNextDeliveryAsync(work.LastAttemptAt))!;
        Assert.Equal(2, secondAttempt.AttemptCount);
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistDeliveryArtifactAsync(
                work.AttemptLease,
                artifact));

        Assert.Equal(
            artifact.FileName,
            GetInternal<string>(delivery, "AttachmentFileName"));
        Assert.Equal(
            artifact.StoragePath,
            GetInternal<string>(delivery, "AttachmentStoragePath"));
    }

    [Fact]
    public async Task ConcurrentDeliveryArtifactWrites_Should_HaveOneAppliedAndNeverCreatePartialOrOverwrite()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        EmailDeliveryWorkItem work = (await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;
        EmailArtifactMetadata first = Artifact("first.xlsx");
        EmailArtifactMetadata second = Artifact("second.xlsx");

        Task<ConditionalEmailMetadataWriteOutcome>[] writes =
        [
            Task.Run(() => store.TryPersistDeliveryArtifactAsync(
                work.AttemptLease,
                first)),
            Task.Run(() => store.TryPersistDeliveryArtifactAsync(
                work.AttemptLease,
                second))
        ];
        await Task.WhenAll(writes);

        Assert.Equal(
            1,
            writes.Count(task =>
                task.Result == ConditionalEmailMetadataWriteOutcome.Applied));
        Assert.Equal(
            1,
            writes.Count(task =>
                task.Result == ConditionalEmailMetadataWriteOutcome.Rejected));
        object delivery = SingleInternal(store, "Deliveries");
        string storedName = GetInternal<string>(delivery, "AttachmentFileName");
        string storedPath = GetInternal<string>(delivery, "AttachmentStoragePath");
        EmailArtifactMetadata winner = storedName == first.FileName
            ? first
            : second;
        Assert.Equal(winner.FileName, storedName);
        Assert.Equal(winner.StoragePath, storedPath);
    }

    [Fact]
    public async Task GetRunArtifactManifestAsync_Should_BeRunScopedOrderedDetachedAndApplyWorkbookRules()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long requestedRunId = await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"),
            OwnerDeliverySeed.Failed("00090622", "OWNER_SCOPE_FAILED"),
            OwnerDeliverySeed.Pending("62010967"));
        IList deliveryStates = ReadInternalList(store, "Deliveries");
        object pending = deliveryStates[0]!;
        object seedFailure = deliveryStates[1]!;
        object unclaimedCancellation = deliveryStates[2]!;
        SetInternalProperty(
            unclaimedCancellation,
            "Status",
            EmailDeliveryStatus.Cancelled);
        (deliveryStates[0], deliveryStates[2]) =
            (deliveryStates[2], deliveryStates[0]);

        EmailRunArtifactManifest incomplete =
            (await store.GetRunArtifactManifestAsync(requestedRunId))!;

        Assert.Equal(
            incomplete.Deliveries
                .Select(entry => entry.EmailDeliveryId)
                .OrderBy(id => id),
            incomplete.Deliveries.Select(entry => entry.EmailDeliveryId));
        Assert.Equal(3, incomplete.Deliveries.Count);
        Assert.Single(incomplete.WorkbookRequiredDeliveries);
        Assert.Equal(
            GetInternal<long>(pending, "EmailDeliveryId"),
            incomplete.WorkbookRequiredDeliveries[0].EmailDeliveryId);
        Assert.False(incomplete.IsZipReady);

        EmailArtifactMetadata artifact = Artifact("owner-1.xlsx");
        SetStoredArtifact(pending, artifact);
        EmailRunArtifactManifest complete =
            (await store.GetRunArtifactManifestAsync(requestedRunId))!;
        Assert.True(complete.IsZipReady);
        SetInternalProperty(pending, "Status", EmailDeliveryStatus.Failed);
        Assert.Equal(
            EmailDeliveryStatus.Pending,
            complete.Deliveries.Single(entry =>
                entry.EmailDeliveryId == GetInternal<long>(
                    pending,
                    "EmailDeliveryId")).Status);

        object requestedRun = ReadInternal(store, "Runs").Single(run =>
            GetInternal<long>(run, "EmailRunId") == requestedRunId);
        SetInternalProperty(requestedRun, "Status", EmailRunStatus.Completed);
        long otherRunId = await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090000"));
        EmailRunArtifactManifest other =
            (await store.GetRunArtifactManifestAsync(otherRunId))!;
        Assert.Single(other.Deliveries);
        Assert.DoesNotContain(
            other.Deliveries,
            entry => entry.EmailDeliveryId == GetInternal<long>(
                seedFailure,
                "EmailDeliveryId"));
        Assert.Null(await store.GetRunArtifactManifestAsync(long.MaxValue));
    }

    [Fact]
    public async Task GetRunIdsReadyForZipAsync_Should_ValidateBoundCancellationAndReturnStableFirstTenWithoutMutation()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        var expected = new List<long>();
        for (int index = 0; index < 12; index++)
        {
            long runId = await CreateProcessingRunAsync(
                store,
                OwnerDeliverySeed.Pending($"C{index + 1:0000000}"));
            object run = ReadInternal(store, "Runs").Last();
            object delivery = ReadInternal(store, "Deliveries").Last();
            SetStoredArtifact(delivery, Artifact($"owner-{runId}.xlsx"));
            SetInternalProperty(run, "Status", EmailRunStatus.Completed);
            expected.Add(runId);
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.GetRunIdsReadyForZipAsync(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.GetRunIdsReadyForZipAsync(101));
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.GetRunIdsReadyForZipAsync(10, source.Token));

        IReadOnlyList<long> result =
            await store.GetRunIdsReadyForZipAsync(10);

        Assert.Equal(expected.Take(10), result);
        Assert.All(ReadInternal(store, "Runs"), run =>
        {
            Assert.Null(GetInternal<string?>(run, "ZipFileName"));
            Assert.Null(GetInternal<string?>(run, "ZipStoragePath"));
        });
    }

    [Fact]
    public async Task GetRunIdsReadyForZipAsync_Should_ReuseManifestWorkbookRulesAndExcludePartialMetadata()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        var runIds = new Dictionary<string, long>(StringComparer.Ordinal);

        async Task<(object Run, object[] Deliveries)> SeedAsync(
            string scenarioName,
            params OwnerDeliverySeed[] seeds)
        {
            long runId = await CreateProcessingRunAsync(store, seeds);
            object run = ReadInternal(store, "Runs").Last();
            object[] deliveries = ReadInternal(store, "Deliveries")
                .Where(item => GetInternal<long>(item, "EmailRunId") == runId)
                .ToArray();
            SetInternalProperty(run, "Status", EmailRunStatus.Completed);
            runIds.Add(scenarioName, runId);
            return (run, deliveries);
        }

        (object processing, object[] processingDeliveries) = await SeedAsync(
            "processing",
            OwnerDeliverySeed.Pending("C1000001"));
        SetStoredArtifact(processingDeliveries[0], Artifact("processing.xlsx"));

        (object review, object[] reviewDeliveries) = await SeedAsync(
            "review",
            OwnerDeliverySeed.Pending("C1000002"));
        SetStoredArtifact(reviewDeliveries[0], Artifact("review.xlsx"));

        (_, object[] mixedTerminal) = await SeedAsync(
            "mixed-terminal",
            OwnerDeliverySeed.Pending("C1000003"),
            OwnerDeliverySeed.Pending("C1000004"));
        SetInternalProperty(
            mixedTerminal[0],
            "Status",
            EmailDeliveryStatus.Failed);
        SetInternalProperty(
            mixedTerminal[1],
            "Status",
            EmailDeliveryStatus.Cancelled);
        SetStoredArtifact(mixedTerminal[1], Artifact("cancelled.xlsx"));

        (_, object[] noWorkbook) = await SeedAsync(
            "no-workbook",
            OwnerDeliverySeed.Failed("C1000005", "OWNER_SCOPE_FAILED"));

        (_, object[] incomplete) = await SeedAsync(
            "incomplete",
            OwnerDeliverySeed.Pending("C1000006"));

        (_, object[] partialAttachment) = await SeedAsync(
            "partial-attachment",
            OwnerDeliverySeed.Pending("C1000007"));
        SetInternalProperty(
            partialAttachment[0],
            "AttachmentFileName",
            "partial.xlsx");

        (object completeZip, object[] completeZipDeliveries) = await SeedAsync(
            "complete-zip",
            OwnerDeliverySeed.Pending("C1000008"));
        SetStoredArtifact(completeZipDeliveries[0], Artifact("zipped.xlsx"));
        EmailArtifactMetadata zip = Artifact("run.zip");
        SetInternalProperty(completeZip, "ZipFileName", zip.FileName);
        SetInternalProperty(completeZip, "ZipStoragePath", zip.StoragePath);

        (object partialZip, object[] partialZipDeliveries) = await SeedAsync(
            "partial-zip",
            OwnerDeliverySeed.Pending("C1000009"));
        SetStoredArtifact(partialZipDeliveries[0], Artifact("partial-zip.xlsx"));
        SetInternalProperty(partialZip, "ZipStoragePath", zip.StoragePath);

        (object pending, object[] pendingDeliveries) = await SeedAsync(
            "pending",
            OwnerDeliverySeed.Pending("C1000010"));
        SetStoredArtifact(pendingDeliveries[0], Artifact("pending.xlsx"));

        (_, object[] acceptedDeliveries) = await SeedAsync(
            "accepted",
            OwnerDeliverySeed.Pending("C1000011"));
        SetInternalProperty(
            acceptedDeliveries[0],
            "Status",
            EmailDeliveryStatus.Accepted);
        SetStoredArtifact(acceptedDeliveries[0], Artifact("accepted.xlsx"));

        (_, object[] simulatedDeliveries) = await SeedAsync(
            "simulated",
            OwnerDeliverySeed.Pending("C1000012"));
        SetInternalProperty(
            simulatedDeliveries[0],
            "Status",
            EmailDeliveryStatus.Simulated);
        SetStoredArtifact(simulatedDeliveries[0], Artifact("simulated.xlsx"));

        (object partial, object[] partialDeliveries) = await SeedAsync(
            "partial-run",
            OwnerDeliverySeed.Pending("C1000013"));
        SetInternalProperty(partial, "Status", EmailRunStatus.Partial);
        SetStoredArtifact(partialDeliveries[0], Artifact("partial-run.xlsx"));

        (object failed, object[] failedDeliveries) = await SeedAsync(
            "failed-run",
            OwnerDeliverySeed.Pending("C1000014"));
        SetInternalProperty(failed, "Status", EmailRunStatus.Failed);
        SetStoredArtifact(failedDeliveries[0], Artifact("failed-run.xlsx"));

        (object cancelled, object[] cancelledDeliveries) = await SeedAsync(
            "cancelled-run",
            OwnerDeliverySeed.Pending("C1000015"));
        SetInternalProperty(cancelled, "Status", EmailRunStatus.Cancelled);
        SetStoredArtifact(
            cancelledDeliveries[0],
            Artifact("cancelled-run.xlsx"));

        SetInternalProperty(processing, "Status", EmailRunStatus.Processing);
        SetInternalProperty(review, "Status", EmailRunStatus.ReviewRequired);
        SetInternalProperty(pending, "Status", EmailRunStatus.Pending);

        IReadOnlyList<long> result =
            await store.GetRunIdsReadyForZipAsync(100);

        Assert.Equal(
            new[]
            {
                runIds["processing"],
                runIds["review"],
                runIds["mixed-terminal"],
                runIds["accepted"],
                runIds["simulated"],
                runIds["partial-run"],
                runIds["failed-run"],
                runIds["cancelled-run"]
            }.OrderBy(id => id),
            result);
        Assert.Single(noWorkbook);
        Assert.Single(incomplete);
    }

    [Fact]
    public async Task TryPersistRunZipArtifactAsync_Should_UseOnlyAttachedTerminalWorkbookWhenAnotherTerminalDeliveryIsFileless()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"),
            OwnerDeliverySeed.Pending("00090622"));
        IList deliveries = ReadInternalList(store, "Deliveries");
        object filelessFailure = deliveries[0]!;
        object attachedCancellation = deliveries[1]!;
        SetInternalProperty(
            filelessFailure,
            "Status",
            EmailDeliveryStatus.Failed);
        SetInternalProperty(filelessFailure, "AttemptCount", 1);
        SetInternalProperty(
            attachedCancellation,
            "Status",
            EmailDeliveryStatus.Cancelled);
        EmailArtifactMetadata workbook = Artifact("owner-2.xlsx");
        SetStoredArtifact(attachedCancellation, workbook);
        EmailRunArtifactManifest expected =
            (await store.GetRunArtifactManifestAsync(runId))!;

        Assert.Equal(
            [GetInternal<long>(attachedCancellation, "EmailDeliveryId")],
            expected.WorkbookRequiredDeliveries.Select(delivery =>
                delivery.EmailDeliveryId));
        Assert.True(expected.IsZipReady);
        EmailArtifactMetadata zip = Artifact("run.zip");

        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Applied,
            await store.TryPersistRunZipArtifactAsync(expected, zip));
        object run = SingleInternal(store, "Runs");
        Assert.Equal(zip.FileName, GetInternal<string>(run, "ZipFileName"));
        Assert.Equal(zip.StoragePath, GetInternal<string>(run, "ZipStoragePath"));
    }

    [Fact]
    public async Task TryPersistRunZipArtifactAsync_Should_KeepLaterTerminalAttachmentInExactContentComparison()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"),
            OwnerDeliverySeed.Pending("00090622"));
        IList deliveries = ReadInternalList(store, "Deliveries");
        object filelessFailure = deliveries[0]!;
        object attachedDelivery = deliveries[1]!;
        SetInternalProperty(
            filelessFailure,
            "Status",
            EmailDeliveryStatus.Failed);
        SetInternalProperty(filelessFailure, "AttemptCount", 1);
        EmailArtifactMetadata workbook = Artifact("owner-2.xlsx");
        SetStoredArtifact(attachedDelivery, workbook);
        EmailRunArtifactManifest expected =
            (await store.GetRunArtifactManifestAsync(runId))!;

        SetInternalProperty(
            attachedDelivery,
            "Status",
            EmailDeliveryStatus.Failed);
        SetInternalProperty(attachedDelivery, "AttemptCount", 1);
        EmailArtifactMetadata zip = Artifact("run.zip");
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Applied,
            await store.TryPersistRunZipArtifactAsync(expected, zip));

        SetStoredArtifact(attachedDelivery, Artifact("owner-2-changed.xlsx"));
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistRunZipArtifactAsync(expected, zip));
    }

    [Fact]
    public async Task ArtifactReadsAndWrites_Should_FailClosedOnPartialStoredMetadata()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        SetInternalProperty(delivery, "AttachmentStoragePath", Artifact(
            "owner-1.xlsx").StoragePath);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.GetRunArtifactManifestAsync(runId));

        SetInternalProperty(delivery, "AttachmentStoragePath", null);
        object run = SingleInternal(store, "Runs");
        SetInternalProperty(run, "ZipFileName", "run.zip");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.GetRunArtifactManifestAsync(runId));
    }

    [Theory]
    [InlineData("PARTIAL_ATTACHMENT")]
    [InlineData("PARTIAL_ATTACHMENT_PATH")]
    [InlineData("PARTIAL_ZIP")]
    [InlineData("PARTIAL_ZIP_PATH")]
    public async Task TryPersistRunZipArtifactAsync_Should_InspectPartialCurrentStateBeforeRejectingIncompleteExpectedManifest(
        string scenario)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        EmailRunArtifactManifest incomplete =
            (await store.GetRunArtifactManifestAsync(runId))!;
        Assert.False(incomplete.IsZipReady);
        if (scenario.StartsWith(
                "PARTIAL_ATTACHMENT",
                StringComparison.Ordinal))
        {
            SetInternalProperty(
                SingleInternal(store, "Deliveries"),
                scenario == "PARTIAL_ATTACHMENT"
                    ? "AttachmentFileName"
                    : "AttachmentStoragePath",
                scenario == "PARTIAL_ATTACHMENT"
                    ? "owner-1.xlsx"
                    : Artifact("owner-1.xlsx").StoragePath);
        }
        else
        {
            SetInternalProperty(
                SingleInternal(store, "Runs"),
                scenario == "PARTIAL_ZIP"
                    ? "ZipFileName"
                    : "ZipStoragePath",
                scenario == "PARTIAL_ZIP"
                    ? "run.zip"
                    : Artifact("run.zip").StoragePath);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.TryPersistRunZipArtifactAsync(
                incomplete,
                Artifact("run.zip")));
    }

    [Theory]
    [InlineData(EmailRunStatus.Processing)]
    [InlineData(EmailRunStatus.ReviewRequired)]
    [InlineData(EmailRunStatus.Completed)]
    [InlineData(EmailRunStatus.Partial)]
    [InlineData(EmailRunStatus.Failed)]
    [InlineData(EmailRunStatus.Cancelled)]
    public async Task TryPersistRunZipArtifactAsync_Should_ApplyAndReplayForEveryAllowedRunStatus(
        EmailRunStatus allowedStatus)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        SetStoredArtifact(delivery, Artifact("owner-1.xlsx"));
        object run = SingleInternal(store, "Runs");
        SetInternalProperty(run, "Status", allowedStatus);
        EmailRunArtifactManifest expected =
            (await store.GetRunArtifactManifestAsync(runId))!;
        EmailArtifactMetadata zip = Artifact("run.zip");

        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Applied,
            await store.TryPersistRunZipArtifactAsync(expected, zip));
        SetInternalProperty(run, "Status", EmailRunStatus.ReviewRequired);
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.AlreadyMatched,
            await store.TryPersistRunZipArtifactAsync(expected, zip));
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistRunZipArtifactAsync(
                expected,
                Artifact("different.zip")));
        Assert.Equal(zip.FileName, GetInternal<string>(run, "ZipFileName"));
        Assert.Equal(zip.StoragePath, GetInternal<string>(run, "ZipStoragePath"));
    }

    [Fact]
    public async Task TryPersistRunZipArtifactAsync_Should_RejectPendingIncompleteAndChangedContent()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        EmailArtifactMetadata first = Artifact("owner-1.xlsx");
        SetStoredArtifact(delivery, first);
        object run = SingleInternal(store, "Runs");
        SetInternalProperty(run, "Status", EmailRunStatus.Pending);
        EmailRunArtifactManifest pending =
            (await store.GetRunArtifactManifestAsync(runId))!;
        Assert.True(pending.IsZipReady);
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistRunZipArtifactAsync(
                pending,
                Artifact("run.zip")));

        SetInternalProperty(run, "Status", EmailRunStatus.Processing);
        EmailRunArtifactManifest expected =
            (await store.GetRunArtifactManifestAsync(runId))!;
        SetStoredArtifact(delivery, Artifact("owner-changed.xlsx"));
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistRunZipArtifactAsync(
                expected,
                Artifact("run.zip")));

        SetInternalProperty(delivery, "AttachmentFileName", null);
        SetInternalProperty(delivery, "AttachmentStoragePath", null);
        EmailRunArtifactManifest incomplete =
            (await store.GetRunArtifactManifestAsync(runId))!;
        Assert.False(incomplete.IsZipReady);
        Assert.Equal(
            ConditionalEmailMetadataWriteOutcome.Rejected,
            await store.TryPersistRunZipArtifactAsync(
                incomplete,
                Artifact("run.zip")));
        Assert.Null(GetInternal<string?>(run, "ZipFileName"));
        Assert.Null(GetInternal<string?>(run, "ZipStoragePath"));
    }

    [Fact]
    public async Task ConcurrentRunZipWrites_Should_HaveExactlyOneAppliedAndNoOverwrite()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        long runId = await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        SetStoredArtifact(
            SingleInternal(store, "Deliveries"),
            Artifact("owner-1.xlsx"));
        EmailRunArtifactManifest expected =
            (await store.GetRunArtifactManifestAsync(runId))!;
        EmailArtifactMetadata first = Artifact("first.zip");
        EmailArtifactMetadata second = Artifact("second.zip");

        Task<ConditionalEmailMetadataWriteOutcome>[] writes =
        [
            Task.Run(() => store.TryPersistRunZipArtifactAsync(
                expected,
                first)),
            Task.Run(() => store.TryPersistRunZipArtifactAsync(
                expected,
                second))
        ];
        await Task.WhenAll(writes);

        Assert.Equal(
            1,
            writes.Count(task =>
                task.Result == ConditionalEmailMetadataWriteOutcome.Applied));
        Assert.Equal(
            1,
            writes.Count(task =>
                task.Result == ConditionalEmailMetadataWriteOutcome.Rejected));
        object run = SingleInternal(store, "Runs");
        string storedName = GetInternal<string>(run, "ZipFileName");
        string storedPath = GetInternal<string>(run, "ZipStoragePath");
        EmailArtifactMetadata winner = storedName == first.FileName
            ? first
            : second;
        Assert.Equal(winner.FileName, storedName);
        Assert.Equal(winner.StoragePath, storedPath);
    }

    [Theory]
    [InlineData(EmailDeliveryStatus.Pending)]
    [InlineData(EmailDeliveryStatus.RetryWait)]
    public async Task TryClaimNextDeliveryAsync_Should_TerminalizeAttempt999AndContinueScanning(
        EmailDeliveryStatus ceilingStatus)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"),
            OwnerDeliverySeed.Pending("00090622"));
        object[] deliveries = ReadInternal(store, "Deliveries").ToArray();
        DateTimeOffset now = Bangkok(2026, 8, 30, 12);
        DateTimeOffset preservedAttempt = now.AddMinutes(-20);
        SetInternalProperty(deliveries[0], "Status", ceilingStatus);
        SetInternalProperty(deliveries[0], "AttemptCount", 999);
        SetInternalProperty(deliveries[0], "LastAttemptAt", preservedAttempt);
        if (ceilingStatus == EmailDeliveryStatus.RetryWait)
        {
            SetInternalProperty(deliveries[0], "NextRetryAt", now);
        }
        object run = SingleInternal(store, "Runs");
        SetInternalProperty(run, "LastErrorCode", "STALE_RUN_ERROR");
        SetInternalProperty(run, "LastErrorMessage", "stale message");

        EmailDeliveryWorkItem work = (await store.TryClaimNextDeliveryAsync(now))!;

        Assert.Equal("00090622", work.OwnerEmployeeNo);
        Assert.Equal(1, work.AttemptCount);
        Assert.Equal(
            EmailDeliveryStatus.Failed,
            GetInternal<EmailDeliveryStatus>(deliveries[0], "Status"));
        Assert.Equal(999, GetInternal<int>(deliveries[0], "AttemptCount"));
        Assert.Equal(
            preservedAttempt,
            GetInternal<DateTimeOffset?>(deliveries[0], "LastAttemptAt"));
        Assert.Null(GetInternal<DateTimeOffset?>(deliveries[0], "NextRetryAt"));
        Assert.Equal(
            "EMAIL_ATTEMPT_LIMIT_EXCEEDED",
            GetInternal<string?>(deliveries[0], "LastErrorCode"));
        Assert.Null(GetInternal<string?>(deliveries[0], "LastErrorMessage"));
        Assert.Equal(
            EmailRunStatus.Processing,
            GetInternal<EmailRunStatus>(run, "Status"));
        Assert.Null(GetInternal<string?>(run, "LastErrorCode"));
        Assert.Null(GetInternal<string?>(run, "LastErrorMessage"));
    }

    [Fact]
    public async Task TryClaimNextDeliveryAsync_Should_DeriveDetachedManualSnapshotBeforeMutation()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        EmailArtifactMetadata artifact = Artifact("manual-owner.xlsx");
        SetStoredArtifact(delivery, artifact);
        SetInternalProperty(delivery, "EffectiveToEmail", " owner@example.com ");
        SetInternalProperty(delivery, "EmailSubject", "Existing subject");
        SetInternalProperty(delivery, "EmailBody", "Existing body");
        SetInternalProperty(delivery, "ResolutionAction", "CONFIRM_NOT_ACCEPTED");
        SetInternalProperty(delivery, "ResolvedByEmployeeNo", "C1008267");
        SetInternalProperty(
            delivery,
            "ResolvedAt",
            Bangkok(2026, 8, 30, 11));

        EmailDeliveryWorkItem work = (await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;

        Assert.True(work.IsManualRetry);
        Assert.Equal(
            "owner@example.com",
            work.PersistedContent!.EffectiveToEmail);
        Assert.Equal("Existing subject", work.PersistedContent.Subject);
        Assert.Equal("Existing body", work.PersistedContent.Body);
        Assert.Equal(artifact, work.Attachment);
    }

    [Fact]
    public async Task TryClaimNextDeliveryAsync_Should_RejectPartialContentAndIncompleteManualSnapshotBeforeMutation()
    {
        (_, DevelopmentEmailExecutionStore partialStore) = CreateStore();
        await CreateProcessingRunAsync(
            partialStore,
            OwnerDeliverySeed.Pending("00090775"));
        object partial = SingleInternal(partialStore, "Deliveries");
        SetInternalProperty(partial, "EffectiveToEmail", "owner@example.com");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            partialStore.TryClaimNextDeliveryAsync(Bangkok(2026, 8, 30, 12)));

        Assert.Equal(
            EmailDeliveryStatus.Pending,
            GetInternal<EmailDeliveryStatus>(partial, "Status"));
        Assert.Equal(0, GetInternal<int>(partial, "AttemptCount"));

        (_, DevelopmentEmailExecutionStore manualStore) = CreateStore();
        await CreateProcessingRunAsync(
            manualStore,
            OwnerDeliverySeed.Pending("00090775"));
        object manual = SingleInternal(manualStore, "Deliveries");
        SetInternalProperty(manual, "EffectiveToEmail", "owner@example.com");
        SetInternalProperty(manual, "EmailSubject", "subject");
        SetInternalProperty(manual, "EmailBody", "body");
        SetInternalProperty(manual, "ResolutionAction", "CONFIRM_NOT_ACCEPTED");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            manualStore.TryClaimNextDeliveryAsync(Bangkok(2026, 8, 30, 12)));

        Assert.Equal(
            EmailDeliveryStatus.Pending,
            GetInternal<EmailDeliveryStatus>(manual, "Status"));
        Assert.Equal(0, GetInternal<int>(manual, "AttemptCount"));
    }

    [Fact]
    public async Task TryRecordPreSubmitFailureAsync_Should_ApplyRetryReplayRejectAndAggregate()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        DateTimeOffset attemptAt = Bangkok(2026, 8, 30, 12);
        EmailDeliveryWorkItem work = (await store.TryClaimNextDeliveryAsync(
            attemptAt))!;
        var failure = new EmailPreSubmitFailure(" EMAIL_EXPORT_FAILED ", " detail ");

        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.Applied,
            await store.TryRecordPreSubmitFailureAsync(work.AttemptLease, failure));
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.AlreadyMatched,
            await store.TryRecordPreSubmitFailureAsync(work.AttemptLease, failure));
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.Rejected,
            await store.TryRecordPreSubmitFailureAsync(
                new EmailDeliveryAttemptLease(
                    work.EmailDeliveryId,
                    work.EmailRunId,
                    work.AttemptCount,
                    work.LastAttemptAt.AddTicks(10)),
                failure));

        object delivery = SingleInternal(store, "Deliveries");
        Assert.Equal(
            EmailDeliveryStatus.RetryWait,
            GetInternal<EmailDeliveryStatus>(delivery, "Status"));
        Assert.Equal(
            attemptAt.ToUniversalTime().AddMinutes(5),
            GetInternal<DateTimeOffset?>(delivery, "NextRetryAt"));
        Assert.Equal(
            "EMAIL_EXPORT_FAILED",
            GetInternal<string?>(delivery, "LastErrorCode"));
        Assert.Equal("detail", GetInternal<string?>(delivery, "LastErrorMessage"));
        object run = SingleInternal(store, "Runs");
        Assert.Equal(
            EmailRunStatus.Processing,
            GetInternal<EmailRunStatus>(run, "Status"));
        Assert.Null(GetInternal<DateTimeOffset?>(run, "CompletedAt"));
        Assert.Null(GetInternal<string?>(run, "LastErrorCode"));
        Assert.Null(GetInternal<string?>(run, "LastErrorMessage"));
    }

    [Fact]
    public async Task TryRecordPreSubmitFailureAsync_Should_ApplyCumulativeFifteenMinutesAfterAttempt2()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        DateTimeOffset firstAttemptAt = Bangkok(2026, 8, 30, 12);
        DateTimeOffset secondAttemptAt = firstAttemptAt.AddMinutes(5);
        SetInternalProperty(delivery, "Status", EmailDeliveryStatus.RetryWait);
        SetInternalProperty(delivery, "AttemptCount", 1);
        SetInternalProperty(delivery, "LastAttemptAt", firstAttemptAt);
        SetInternalProperty(delivery, "NextRetryAt", secondAttemptAt);

        EmailDeliveryWorkItem secondAttempt =
            (await store.TryClaimNextDeliveryAsync(secondAttemptAt))!;
        ConditionalEmailDeliveryTransitionOutcome outcome =
            await store.TryRecordPreSubmitFailureAsync(
                secondAttempt.AttemptLease,
                new EmailPreSubmitFailure("EMAIL_EXPORT_FAILED", null));

        Assert.Equal(2, secondAttempt.AttemptCount);
        Assert.Equal(secondAttemptAt.ToUniversalTime(), secondAttempt.LastAttemptAt);
        Assert.Equal(ConditionalEmailDeliveryTransitionOutcome.Applied, outcome);
        Assert.Equal(
            firstAttemptAt.ToUniversalTime().AddMinutes(15),
            GetInternal<DateTimeOffset?>(delivery, "NextRetryAt"));
    }

    [Fact]
    public async Task TryRecordPreSubmitFailureAsync_Should_RejectEveryFullLeaseAndAbaMismatch()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        EmailDeliveryWorkItem work = (await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;
        var failure = new EmailPreSubmitFailure(
            "EMAIL_EXPORT_FAILED",
            "detail");

        foreach (EmailDeliveryAttemptLease mismatch in EveryLeaseMismatch(
            work.AttemptLease))
        {
            Assert.Equal(
                ConditionalEmailDeliveryTransitionOutcome.Rejected,
                await store.TryRecordPreSubmitFailureAsync(mismatch, failure));
        }

        object delivery = SingleInternal(store, "Deliveries");
        Assert.Equal(
            EmailDeliveryStatus.Preparing,
            GetInternal<EmailDeliveryStatus>(delivery, "Status"));
        Assert.Null(GetInternal<DateTimeOffset?>(delivery, "NextRetryAt"));
        Assert.Null(GetInternal<string?>(delivery, "LastErrorCode"));
        Assert.Null(GetInternal<string?>(delivery, "LastErrorMessage"));
    }

    [Fact]
    public async Task TryBeginSubmissionAsync_Should_PreserveManualSnapshotRejectMismatchesAndRequireFullLease()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        EmailArtifactMetadata artifact = Artifact("manual-owner.xlsx");
        SetStoredArtifact(delivery, artifact);
        SetInternalProperty(delivery, "EffectiveToEmail", " owner@example.com ");
        SetInternalProperty(delivery, "EmailSubject", "Existing subject");
        SetInternalProperty(delivery, "EmailBody", "Existing body");
        SetInternalProperty(delivery, "ResolutionAction", "CONFIRM_NOT_ACCEPTED");
        SetInternalProperty(delivery, "ResolvedByEmployeeNo", "C1008267");
        DateTimeOffset resolvedAt = Bangkok(2026, 8, 30, 11);
        SetInternalProperty(delivery, "ResolvedAt", resolvedAt);
        EmailDeliveryWorkItem work = (await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;
        DateTimeOffset submitStartedAt = Bangkok(2026, 8, 30, 12).AddMinutes(1);
        var exact = new EmailSubmissionSnapshot(
            work.PersistedContent!,
            work.Attachment!,
            submitStartedAt);
        EmailSubmissionSnapshot[] snapshotMismatches =
        [
            new(
                new EmailDeliveryContentSnapshot(
                    "other@example.com",
                    work.PersistedContent!.Subject,
                    work.PersistedContent.Body),
                work.Attachment!,
                submitStartedAt),
            new(
                new EmailDeliveryContentSnapshot(
                    work.PersistedContent!.EffectiveToEmail,
                    "Changed subject",
                    work.PersistedContent.Body),
                work.Attachment!,
                submitStartedAt),
            new(
                new EmailDeliveryContentSnapshot(
                    work.PersistedContent!.EffectiveToEmail,
                    work.PersistedContent.Subject,
                    "Changed body"),
                work.Attachment!,
                submitStartedAt),
            new(
                work.PersistedContent!,
                Artifact("other-owner.xlsx"),
                submitStartedAt)
        ];

        foreach (EmailSubmissionSnapshot mismatch in snapshotMismatches)
        {
            Assert.Equal(
                ConditionalEmailDeliveryTransitionOutcome.Rejected,
                await store.TryBeginSubmissionAsync(work.AttemptLease, mismatch));
        }
        foreach (EmailDeliveryAttemptLease mismatch in EveryLeaseMismatch(
            work.AttemptLease))
        {
            Assert.Equal(
                ConditionalEmailDeliveryTransitionOutcome.Rejected,
                await store.TryBeginSubmissionAsync(mismatch, exact));
        }

        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.Applied,
            await store.TryBeginSubmissionAsync(work.AttemptLease, exact));
        Assert.Equal(
            EmailDeliveryStatus.Submitting,
            GetInternal<EmailDeliveryStatus>(delivery, "Status"));
        Assert.Equal(
            " owner@example.com ",
            GetInternal<string>(delivery, "EffectiveToEmail"));
        Assert.Equal(
            "Existing subject",
            GetInternal<string>(delivery, "EmailSubject"));
        Assert.Equal(
            "Existing body",
            GetInternal<string>(delivery, "EmailBody"));
        Assert.Equal(
            artifact.FileName,
            GetInternal<string>(delivery, "AttachmentFileName"));
        Assert.Equal(
            artifact.StoragePath,
            GetInternal<string>(delivery, "AttachmentStoragePath"));
        Assert.Equal(
            "CONFIRM_NOT_ACCEPTED",
            GetInternal<string>(delivery, "ResolutionAction"));
        Assert.Equal(
            "C1008267",
            GetInternal<string>(delivery, "ResolvedByEmployeeNo"));
        Assert.Equal(
            resolvedAt,
            GetInternal<DateTimeOffset?>(delivery, "ResolvedAt"));
    }

    [Fact]
    public async Task TryMarkSimulatedAsync_Should_RejectEveryFullLeaseAndAbaMismatch()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        EmailArtifactMetadata artifact = Artifact("fake-lease.xlsx");
        SetStoredArtifact(delivery, artifact);
        EmailDeliveryWorkItem work = (await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;
        var snapshot = new EmailSubmissionSnapshot(
            new EmailDeliveryContentSnapshot(
                "safe@example.com",
                "subject",
                "body"),
            artifact,
            Bangkok(2026, 8, 30, 12));
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.Applied,
            await store.TryBeginSubmissionAsync(work.AttemptLease, snapshot));

        foreach (EmailDeliveryAttemptLease mismatch in EveryLeaseMismatch(
            work.AttemptLease))
        {
            Assert.Equal(
                ConditionalEmailDeliveryTransitionOutcome.Rejected,
                await store.TryMarkSimulatedAsync(mismatch));
        }

        Assert.Equal(
            EmailDeliveryStatus.Submitting,
            GetInternal<EmailDeliveryStatus>(delivery, "Status"));
    }

    [Theory]
    [InlineData("ACCEPTED")]
    [InlineData("UNKNOWN")]
    public async Task ApiResultTransition_Should_RejectEveryFullLeaseAndAbaMismatch(
        string transition)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        var apiConfiguration = new EmailConfigurationSnapshot(
            "ORACLE",
            "API_EMAIL",
            "SAFE_REDIRECT");
        await CreateProcessingRunAsync(
            store,
            apiConfiguration,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        EmailArtifactMetadata artifact = Artifact("api-lease.xlsx");
        SetStoredArtifact(delivery, artifact);
        EmailDeliveryWorkItem work = (await store.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;
        var snapshot = new EmailSubmissionSnapshot(
            new EmailDeliveryContentSnapshot(
                "safe@example.com",
                "subject",
                "body"),
            artifact,
            Bangkok(2026, 8, 30, 12));
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.Applied,
            await store.TryBeginSubmissionAsync(work.AttemptLease, snapshot));
        var accepted = new EmailSubmissionAccepted(
            "api-email-lease",
            202,
            Bangkok(2026, 8, 30, 12));
        var unknown = new EmailSubmissionUnknown(
            504,
            "API_EMAIL_TIMEOUT",
            "timeout");

        foreach (EmailDeliveryAttemptLease mismatch in EveryLeaseMismatch(
            work.AttemptLease))
        {
            ConditionalEmailDeliveryTransitionOutcome outcome =
                transition == "ACCEPTED"
                    ? await store.TryRecordAcceptedAsync(mismatch, accepted)
                    : await store.TryRecordUnknownAsync(mismatch, unknown);
            Assert.Equal(ConditionalEmailDeliveryTransitionOutcome.Rejected, outcome);
        }

        Assert.Equal(
            EmailDeliveryStatus.Submitting,
            GetInternal<EmailDeliveryStatus>(delivery, "Status"));
        Assert.Null(GetInternal<string?>(delivery, "ExternalRequestId"));
        Assert.Null(GetInternal<int?>(delivery, "LastHttpStatus"));
        Assert.Null(GetInternal<DateTimeOffset?>(delivery, "AcceptedAt"));
        Assert.Null(GetInternal<string?>(delivery, "LastErrorCode"));
        Assert.Null(GetInternal<string?>(delivery, "LastErrorMessage"));
    }

    [Fact]
    public async Task SubmissionTransitions_Should_EnforceModesSnapshotsUniquenessAndAtomicAggregation()
    {
        (_, DevelopmentEmailExecutionStore fakeStore) = CreateStore();
        await CreateProcessingRunAsync(
            fakeStore,
            OwnerDeliverySeed.Pending("00090775"));
        object fakeDelivery = SingleInternal(fakeStore, "Deliveries");
        EmailArtifactMetadata fakeArtifact = Artifact("fake.xlsx");
        SetStoredArtifact(fakeDelivery, fakeArtifact);
        EmailDeliveryWorkItem fakeWork = (await fakeStore.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;
        var fakeSnapshot = new EmailSubmissionSnapshot(
            new EmailDeliveryContentSnapshot(
                "safe@example.com",
                "subject",
                "body"),
            fakeArtifact,
            Bangkok(2026, 8, 30, 12).AddTicks(19));

        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.Applied,
            await fakeStore.TryBeginSubmissionAsync(
                fakeWork.AttemptLease,
                fakeSnapshot));
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.AlreadyMatched,
            await fakeStore.TryBeginSubmissionAsync(
                fakeWork.AttemptLease,
                fakeSnapshot));
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.Rejected,
            await fakeStore.TryRecordAcceptedAsync(
                fakeWork.AttemptLease,
                new EmailSubmissionAccepted(
                    "external-1",
                    202,
                    Bangkok(2026, 8, 30, 12))));
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.Applied,
            await fakeStore.TryMarkSimulatedAsync(fakeWork.AttemptLease));
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.AlreadyMatched,
            await fakeStore.TryMarkSimulatedAsync(fakeWork.AttemptLease));
        object fakeRun = SingleInternal(fakeStore, "Runs");
        Assert.Equal(
            EmailRunStatus.Completed,
            GetInternal<EmailRunStatus>(fakeRun, "Status"));
        Assert.NotNull(GetInternal<DateTimeOffset?>(fakeRun, "CompletedAt"));

        (_, DevelopmentEmailExecutionStore apiStore) = CreateStore();
        var apiConfiguration = new EmailConfigurationSnapshot(
            "ORACLE",
            "API_EMAIL",
            "SAFE_REDIRECT");
        await CreateProcessingRunAsync(
            apiStore,
            apiConfiguration,
            OwnerDeliverySeed.Pending("00090775"),
            OwnerDeliverySeed.Pending("00090622"));
        object[] apiDeliveries = ReadInternal(apiStore, "Deliveries").ToArray();
        EmailArtifactMetadata firstArtifact = Artifact("api-1.xlsx");
        SetStoredArtifact(apiDeliveries[0], firstArtifact);
        EmailDeliveryWorkItem first = (await apiStore.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;
        var firstSnapshot = new EmailSubmissionSnapshot(
            new EmailDeliveryContentSnapshot(
                "safe@example.com",
                "subject",
                "body"),
            firstArtifact,
            Bangkok(2026, 8, 30, 12));
        await apiStore.TryBeginSubmissionAsync(first.AttemptLease, firstSnapshot);
        var accepted = new EmailSubmissionAccepted(
            "opaque-request-id",
            202,
            Bangkok(2026, 8, 30, 12));
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.Applied,
            await apiStore.TryRecordAcceptedAsync(first.AttemptLease, accepted));
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.AlreadyMatched,
            await apiStore.TryRecordAcceptedAsync(first.AttemptLease, accepted));

        EmailArtifactMetadata secondArtifact = Artifact("api-2.xlsx");
        SetStoredArtifact(apiDeliveries[1], secondArtifact);
        EmailDeliveryWorkItem second = (await apiStore.TryClaimNextDeliveryAsync(
            Bangkok(2026, 8, 30, 12)))!;
        var secondSnapshot = new EmailSubmissionSnapshot(
            new EmailDeliveryContentSnapshot(
                "safe@example.com",
                "subject",
                "body"),
            secondArtifact,
            Bangkok(2026, 8, 30, 12));
        await apiStore.TryBeginSubmissionAsync(second.AttemptLease, secondSnapshot);
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.Rejected,
            await apiStore.TryRecordAcceptedAsync(second.AttemptLease, accepted));
        var unknown = new EmailSubmissionUnknown(
            503,
            "API_EMAIL_UNKNOWN",
            "response lost");
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.Applied,
            await apiStore.TryRecordUnknownAsync(second.AttemptLease, unknown));
        Assert.Equal(
            ConditionalEmailDeliveryTransitionOutcome.AlreadyMatched,
            await apiStore.TryRecordUnknownAsync(second.AttemptLease, unknown));
        object apiRun = SingleInternal(apiStore, "Runs");
        Assert.Equal(
            EmailRunStatus.ReviewRequired,
            GetInternal<EmailRunStatus>(apiRun, "Status"));
        Assert.Null(GetInternal<DateTimeOffset?>(apiRun, "CompletedAt"));
    }

    [Fact]
    public async Task RecoverStalePreparingDeliveriesAsync_Should_UseInclusiveStableBoundedRetryPolicy()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"),
            OwnerDeliverySeed.Pending("00090622"),
            OwnerDeliverySeed.Pending("62010967"));
        object[] deliveries = ReadInternal(store, "Deliveries").ToArray();
        DateTimeOffset cutoff = Bangkok(2026, 8, 30, 12);
        for (int index = 0; index < deliveries.Length; index++)
        {
            SetInternalProperty(deliveries[index], "Status", EmailDeliveryStatus.Preparing);
            SetInternalProperty(deliveries[index], "AttemptCount", 1);
            SetInternalProperty(
                deliveries[index],
                "LastAttemptAt",
                index == 2 ? cutoff.AddTicks(1) : cutoff);
        }

        Assert.Equal(
            1,
            await store.RecoverStalePreparingDeliveriesAsync(cutoff, 1));
        Assert.Equal(
            EmailDeliveryStatus.RetryWait,
            GetInternal<EmailDeliveryStatus>(deliveries[0], "Status"));
        Assert.Equal(
            cutoff.AddMinutes(5),
            GetInternal<DateTimeOffset?>(deliveries[0], "NextRetryAt"));
        Assert.Equal(
            "EMAIL_PREPARING_STALE",
            GetInternal<string?>(deliveries[0], "LastErrorCode"));
        Assert.Equal(
            EmailDeliveryStatus.Preparing,
            GetInternal<EmailDeliveryStatus>(deliveries[1], "Status"));

        Assert.Equal(
            1,
            await store.RecoverStalePreparingDeliveriesAsync(cutoff, 10));
        Assert.Equal(
            EmailDeliveryStatus.RetryWait,
            GetInternal<EmailDeliveryStatus>(deliveries[1], "Status"));
        Assert.Equal(
            EmailDeliveryStatus.Preparing,
            GetInternal<EmailDeliveryStatus>(deliveries[2], "Status"));
    }

    [Fact]
    public async Task MarkStuckSubmittingUnknownAsync_Should_UseApiEmailCutoffAndDeterministicLimit()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        var apiSnapshot = new EmailConfigurationSnapshot(
            "ORACLE",
            "API_EMAIL",
            "SAFE_REDIRECT");
        await CreateProcessingRunAsync(
            store,
            apiSnapshot,
            OwnerDeliverySeed.Pending("00090775"),
            OwnerDeliverySeed.Pending("00090622"),
            OwnerDeliverySeed.Pending("62010967"),
            OwnerDeliverySeed.Pending("62010691"));
        object[] deliveries = ReadInternal(store, "Deliveries").ToArray();
        DateTimeOffset cutoff = Bangkok(2026, 8, 30, 12);
        foreach (object delivery in deliveries)
        {
            SetInternalProperty(delivery, "Status", EmailDeliveryStatus.Submitting);
        }
        SetInternalProperty(deliveries[0], "SubmitStartedAt", cutoff.AddMinutes(-2));
        SetInternalProperty(deliveries[1], "SubmitStartedAt", cutoff);
        SetInternalProperty(deliveries[2], "SubmitStartedAt", null);
        SetInternalProperty(deliveries[3], "SubmitStartedAt", cutoff.AddTicks(1));

        int first = await store.MarkStuckSubmittingUnknownAsync(cutoff, 1);
        Assert.Equal(1, first);
        Assert.Equal(
            EmailDeliveryStatus.Unknown,
            GetInternal<EmailDeliveryStatus>(deliveries[0], "Status"));
        Assert.Equal(
            EmailDeliveryStatus.Submitting,
            GetInternal<EmailDeliveryStatus>(deliveries[1], "Status"));

        int second = await store.MarkStuckSubmittingUnknownAsync(cutoff, 10);

        Assert.Equal(1, second);
        Assert.Equal(
            2,
            deliveries.Count(delivery => GetInternal<EmailDeliveryStatus>(
                delivery,
                "Status") == EmailDeliveryStatus.Unknown));
        Assert.Equal(
            EmailDeliveryStatus.Unknown,
            GetInternal<EmailDeliveryStatus>(deliveries[0], "Status"));
        Assert.Equal(
            EmailDeliveryStatus.Unknown,
            GetInternal<EmailDeliveryStatus>(deliveries[1], "Status"));
        Assert.Equal(
            EmailDeliveryStatus.Submitting,
            GetInternal<EmailDeliveryStatus>(deliveries[2], "Status"));
        Assert.Equal(
            EmailDeliveryStatus.Submitting,
            GetInternal<EmailDeliveryStatus>(deliveries[3], "Status"));
        object run = SingleInternal(store, "Runs");
        Assert.Equal(
            EmailRunStatus.Processing,
            GetInternal<EmailRunStatus>(run, "Status"));
        Assert.Null(GetInternal<DateTimeOffset?>(run, "CompletedAt"));
        Assert.Null(GetInternal<string?>(run, "LastErrorCode"));
        Assert.Null(GetInternal<string?>(run, "LastErrorMessage"));
    }

    [Fact]
    public async Task MarkStuckSubmittingUnknownAsync_Should_NeverChangeFakeDelivery()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        DateTimeOffset cutoff = Bangkok(2026, 8, 30, 12);
        SetInternalProperty(delivery, "Status", EmailDeliveryStatus.Submitting);
        SetInternalProperty(
            delivery,
            "SubmitStartedAt",
            cutoff.AddMinutes(-10));

        int changed = await store.MarkStuckSubmittingUnknownAsync(cutoff, 10);

        Assert.Equal(0, changed);
        Assert.Equal(
            EmailDeliveryStatus.Submitting,
            GetInternal<EmailDeliveryStatus>(delivery, "Status"));
    }

    [Fact]
    public async Task MarkStuckSubmittingUnknownAsync_Should_AggregateFinalRunToReviewRequired()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        var apiSnapshot = new EmailConfigurationSnapshot(
            "ORACLE",
            "API_EMAIL",
            "SAFE_REDIRECT");
        await CreateProcessingRunAsync(
            store,
            apiSnapshot,
            OwnerDeliverySeed.Pending("00090775"));
        object delivery = SingleInternal(store, "Deliveries");
        DateTimeOffset cutoff = Bangkok(2026, 8, 30, 12);
        SetInternalProperty(delivery, "Status", EmailDeliveryStatus.Submitting);
        SetInternalProperty(delivery, "SubmitStartedAt", cutoff);
        object run = SingleInternal(store, "Runs");
        SetInternalProperty(run, "LastErrorCode", "STALE_RUN_ERROR");
        SetInternalProperty(run, "LastErrorMessage", "stale message");

        Assert.Equal(
            1,
            await store.MarkStuckSubmittingUnknownAsync(cutoff, 10));

        Assert.Equal(
            EmailRunStatus.ReviewRequired,
            GetInternal<EmailRunStatus>(run, "Status"));
        Assert.Null(GetInternal<DateTimeOffset?>(run, "CompletedAt"));
        Assert.Null(GetInternal<string?>(run, "LastErrorCode"));
        Assert.Null(GetInternal<string?>(run, "LastErrorMessage"));
    }

    [Theory]
    [InlineData(
        EmailUnknownResolutionAction.ConfirmAccepted,
        EmailDeliveryStatus.Accepted,
        EmailRunStatus.Completed)]
    [InlineData(
        EmailUnknownResolutionAction.ConfirmNotAccepted,
        EmailDeliveryStatus.Failed,
        EmailRunStatus.Failed)]
    [InlineData(
        EmailUnknownResolutionAction.Cancel,
        EmailDeliveryStatus.Cancelled,
        EmailRunStatus.Cancelled)]
    public async Task ResolveUnknown_Should_ApplyAllActionsPreserveEvidenceAndAggregate(
        EmailUnknownResolutionAction action,
        EmailDeliveryStatus expectedDeliveryStatus,
        EmailRunStatus expectedRunStatus)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object run, object delivery) = await SeedUnknownAsync(store);
        DateTimeOffset submitStartedAt = GetInternal<DateTimeOffset?>(
            delivery,
            "SubmitStartedAt")!.Value;
        DateTimeOffset lastAttemptAt = GetInternal<DateTimeOffset?>(
            delivery,
            "LastAttemptAt")!.Value;
        DateTimeOffset resolvedAt = new(
            2026,
            8,
            31,
            4,
            5,
            6,
            TimeSpan.Zero);
        string? externalId = action ==
            EmailUnknownResolutionAction.ConfirmAccepted
                ? "api-request-71"
                : null;

        ResolveUnknownDeliveryResult result =
            await store.TryResolveUnknownDeliveryAsync(
                new ResolveUnknownDeliveryCommand(
                    1,
                    2,
                    action,
                    externalId,
                    "C1008267"),
                resolvedAt);

        Assert.Equal(ResolveUnknownDeliveryOutcome.Applied, result.Outcome);
        Assert.Equal(expectedDeliveryStatus, result.DeliveryStatus);
        Assert.Equal(expectedRunStatus, result.RunStatus);
        Assert.Equal(expectedDeliveryStatus, GetInternal<EmailDeliveryStatus>(
            delivery,
            "Status"));
        Assert.Equal(504, GetInternal<int?>(delivery, "LastHttpStatus"));
        Assert.Equal("API_EMAIL_TIMEOUT", GetInternal<string?>(
            delivery,
            "LastErrorCode"));
        Assert.Equal("provider result uncertain", GetInternal<string?>(
            delivery,
            "LastErrorMessage"));
        Assert.Equal(submitStartedAt, GetInternal<DateTimeOffset?>(
            delivery,
            "SubmitStartedAt"));
        Assert.Equal(lastAttemptAt, GetInternal<DateTimeOffset?>(
            delivery,
            "LastAttemptAt"));
        Assert.Equal(2, GetInternal<int>(delivery, "AttemptCount"));
        Assert.Equal(action switch
        {
            EmailUnknownResolutionAction.ConfirmAccepted => "CONFIRM_ACCEPTED",
            EmailUnknownResolutionAction.ConfirmNotAccepted =>
                "CONFIRM_NOT_ACCEPTED",
            EmailUnknownResolutionAction.Cancel => "CANCEL",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        }, GetInternal<string?>(delivery, "ResolutionAction"));
        Assert.Equal("C1008267", GetInternal<string?>(
            delivery,
            "ResolvedByEmployeeNo"));
        Assert.Equal(resolvedAt, GetInternal<DateTimeOffset?>(
            delivery,
            "ResolvedAt"));
        Assert.Equal(externalId, GetInternal<string?>(
            delivery,
            "ExternalRequestId"));
        Assert.Equal(
            action == EmailUnknownResolutionAction.ConfirmAccepted
                ? resolvedAt
                : null,
            GetInternal<DateTimeOffset?>(delivery, "AcceptedAt"));
        Assert.Equal(expectedRunStatus, GetInternal<EmailRunStatus>(run, "Status"));
        Assert.Null(GetInternal<string?>(run, "LastErrorCode"));
        Assert.Null(GetInternal<string?>(run, "LastErrorMessage"));
    }

    [Fact]
    public async Task ResolveUnknown_Should_UseVersionBeforeReplayAndPreserveOriginalResolver()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (_, object delivery) = await SeedUnknownAsync(store);
        DateTimeOffset firstResolvedAt = new(
            2026,
            8,
            31,
            4,
            5,
            6,
            TimeSpan.Zero);
        var first = new ResolveUnknownDeliveryCommand(
            1,
            2,
            EmailUnknownResolutionAction.ConfirmAccepted,
            "api-request-71",
            "C1008267");
        Assert.Equal(
            ResolveUnknownDeliveryOutcome.Applied,
            (await store.TryResolveUnknownDeliveryAsync(
                first,
                firstResolvedAt)).Outcome);

        ResolveUnknownDeliveryResult replay =
            await store.TryResolveUnknownDeliveryAsync(
                new ResolveUnknownDeliveryCommand(
                    1,
                    2,
                    EmailUnknownResolutionAction.ConfirmAccepted,
                    "api-request-71",
                    "A0000001"),
                firstResolvedAt.AddHours(1));
        ResolveUnknownDeliveryResult delayed =
            await store.TryResolveUnknownDeliveryAsync(
                new ResolveUnknownDeliveryCommand(
                    1,
                    1,
                    EmailUnknownResolutionAction.ConfirmAccepted,
                    "api-request-71",
                    "A0000001"),
                firstResolvedAt.AddHours(2));

        Assert.Equal(ResolveUnknownDeliveryOutcome.AlreadyMatched, replay.Outcome);
        Assert.Equal(ResolveUnknownDeliveryOutcome.Rejected, delayed.Outcome);
        Assert.Equal("EMAIL_DELIVERY_VERSION_CONFLICT", delayed.ErrorCode);
        Assert.Equal("C1008267", GetInternal<string?>(
            delivery,
            "ResolvedByEmployeeNo"));
        Assert.Equal(firstResolvedAt, GetInternal<DateTimeOffset?>(
            delivery,
            "ResolvedAt"));
    }

    [Fact]
    public async Task ResolveUnknown_Should_NotReplayForgedFakeTransport()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await CreateProcessingRunAsync(
            store,
            OwnerDeliverySeed.Pending("C1000001"));
        object run = SingleInternal(store, "Runs");
        object delivery = SingleInternal(store, "Deliveries");
        SetInternalProperty(delivery, "AttemptCount", 1);
        SetInternalProperty(delivery, "Status", EmailDeliveryStatus.Accepted);
        SetInternalProperty(delivery, "AcceptedAt", UtcNow);
        SetInternalProperty(delivery, "ExternalRequestId", "forged-id");
        SetInternalProperty(delivery, "ResolutionAction", "CONFIRM_ACCEPTED");
        SetInternalProperty(delivery, "ResolvedByEmployeeNo", "C1008267");
        SetInternalProperty(delivery, "ResolvedAt", UtcNow);
        SetInternalProperty(run, "Status", EmailRunStatus.Completed);

        ResolveUnknownDeliveryResult result =
            await store.TryResolveUnknownDeliveryAsync(
                new ResolveUnknownDeliveryCommand(
                    1,
                    1,
                    EmailUnknownResolutionAction.ConfirmAccepted,
                    "forged-id",
                    "C1008267"),
                UtcNow.AddMinutes(1));

        Assert.Equal(ResolveUnknownDeliveryOutcome.Rejected, result.Outcome);
        Assert.Equal("EMAIL_DELIVERY_NOT_UNKNOWN", result.ErrorCode);
    }

    [Fact]
    public async Task ResolveUnknown_Should_ReplaceRetainedManualResolutionOnSecondUnknown()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object run, object delivery) = await SeedUnknownAsync(store);
        DateTimeOffset firstResolvedAt = new(
            2026,
            8,
            31,
            4,
            5,
            6,
            TimeSpan.Zero);
        var action = new ResolveUnknownDeliveryCommand(
            1,
            2,
            EmailUnknownResolutionAction.ConfirmNotAccepted,
            externalRequestId: null,
            "C1008267");
        await store.TryResolveUnknownDeliveryAsync(action, firstResolvedAt);
        SetInternalProperty(delivery, "Status", EmailDeliveryStatus.Unknown);
        SetInternalProperty(delivery, "AttemptCount", 3);
        SetInternalProperty(run, "Status", EmailRunStatus.ReviewRequired);
        DateTimeOffset secondResolvedAt = firstResolvedAt.AddHours(1);

        ResolveUnknownDeliveryResult result =
            await store.TryResolveUnknownDeliveryAsync(
                new ResolveUnknownDeliveryCommand(
                    1,
                    3,
                    EmailUnknownResolutionAction.ConfirmNotAccepted,
                    externalRequestId: null,
                    "A0000001"),
                secondResolvedAt);

        Assert.Equal(ResolveUnknownDeliveryOutcome.Applied, result.Outcome);
        Assert.Equal("A0000001", GetInternal<string?>(
            delivery,
            "ResolvedByEmployeeNo"));
        Assert.Equal(secondResolvedAt, GetInternal<DateTimeOffset?>(
            delivery,
            "ResolvedAt"));
    }

    [Fact]
    public async Task ResolveUnknown_Should_RejectPartialResolutionAndDuplicateExternalIdWithoutMutation()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object run, object first) = await SeedUnknownAsync(
            store,
            OwnerDeliverySeed.Pending("C1000001"),
            OwnerDeliverySeed.Pending("C1000002"));
        object second = ReadInternal(store, "Deliveries").Last();
        SetInternalProperty(first, "ResolutionAction", "CONFIRM_NOT_ACCEPTED");

        ResolveUnknownDeliveryResult invalid =
            await store.TryResolveUnknownDeliveryAsync(
                new ResolveUnknownDeliveryCommand(
                    1,
                    2,
                    EmailUnknownResolutionAction.Cancel,
                    externalRequestId: null,
                    "C1008267"),
                UtcNow);

        Assert.Equal("EMAIL_RESOLUTION_SNAPSHOT_INVALID", invalid.ErrorCode);
        SetInternalProperty(first, "ResolutionAction", null);
        SetInternalProperty(second, "Status", EmailDeliveryStatus.Accepted);
        SetInternalProperty(second, "ExternalRequestId", "duplicate-id");
        ResolveUnknownDeliveryResult duplicate =
            await store.TryResolveUnknownDeliveryAsync(
                new ResolveUnknownDeliveryCommand(
                    1,
                    2,
                    EmailUnknownResolutionAction.ConfirmAccepted,
                    "duplicate-id",
                    "C1008267"),
                UtcNow);

        Assert.Equal("EMAIL_EXTERNAL_REQUEST_ID_CONFLICT", duplicate.ErrorCode);
        Assert.Equal(EmailDeliveryStatus.Unknown, GetInternal<EmailDeliveryStatus>(
            first,
            "Status"));
        Assert.Equal(EmailRunStatus.ReviewRequired, GetInternal<EmailRunStatus>(
            run,
            "Status"));
    }

    [Fact]
    public async Task ResolveUnknown_Should_ClassifyPartialSnapshotBeforeStatusConflict()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (_, object delivery) = await SeedUnknownAsync(store);
        SetInternalProperty(delivery, "Status", EmailDeliveryStatus.Failed);
        SetInternalProperty(delivery, "ResolutionAction", "CONFIRM_NOT_ACCEPTED");

        ResolveUnknownDeliveryResult result =
            await store.TryResolveUnknownDeliveryAsync(
                new ResolveUnknownDeliveryCommand(
                    1,
                    2,
                    EmailUnknownResolutionAction.ConfirmNotAccepted,
                    externalRequestId: null,
                    "C1008267"),
                UtcNow);

        Assert.Equal("EMAIL_RESOLUTION_SNAPSHOT_INVALID", result.ErrorCode);
    }

    [Theory]
    [InlineData(EmailUnknownResolutionAction.ConfirmNotAccepted)]
    [InlineData(EmailUnknownResolutionAction.Cancel)]
    public async Task ResolveUnknown_Should_NotReplayTerminalShapeWithAcceptedFields(
        EmailUnknownResolutionAction action)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (_, object delivery) = await SeedUnknownAsync(store);
        var command = new ResolveUnknownDeliveryCommand(
            1,
            2,
            action,
            externalRequestId: null,
            "C1008267");
        Assert.Equal(
            ResolveUnknownDeliveryOutcome.Applied,
            (await store.TryResolveUnknownDeliveryAsync(command, UtcNow)).Outcome);
        SetInternalProperty(delivery, "ExternalRequestId", "forged-id");
        SetInternalProperty(delivery, "AcceptedAt", UtcNow);

        ResolveUnknownDeliveryResult result =
            await store.TryResolveUnknownDeliveryAsync(
                command,
                UtcNow.AddMinutes(1));

        Assert.Equal(ResolveUnknownDeliveryOutcome.Rejected, result.Outcome);
    }

    [Theory]
    [InlineData(
        EmailUnknownResolutionAction.ConfirmNotAccepted,
        EmailDeliveryStatus.Failed,
        EmailRunStatus.Failed)]
    [InlineData(
        EmailUnknownResolutionAction.Cancel,
        EmailDeliveryStatus.Cancelled,
        EmailRunStatus.Cancelled)]
    public async Task ResolveUnknown_Should_ReplayExactFailedAndCancelledTerminalShapes(
        EmailUnknownResolutionAction action,
        EmailDeliveryStatus expectedDeliveryStatus,
        EmailRunStatus expectedRunStatus)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (_, object delivery) = await SeedUnknownAsync(store);
        DateTimeOffset resolvedAt = UtcNow;
        var command = new ResolveUnknownDeliveryCommand(
            1,
            2,
            action,
            externalRequestId: null,
            "C1008267");
        Assert.Equal(
            ResolveUnknownDeliveryOutcome.Applied,
            (await store.TryResolveUnknownDeliveryAsync(command, resolvedAt)).Outcome);

        ResolveUnknownDeliveryResult replay =
            await store.TryResolveUnknownDeliveryAsync(
                command,
                resolvedAt.AddMinutes(5));

        Assert.Equal(ResolveUnknownDeliveryOutcome.AlreadyMatched, replay.Outcome);
        Assert.Equal(expectedDeliveryStatus, replay.DeliveryStatus);
        Assert.Equal(expectedRunStatus, replay.RunStatus);
        Assert.Equal("C1008267", GetInternal<string?>(
            delivery,
            "ResolvedByEmployeeNo"));
        Assert.Equal(resolvedAt, GetInternal<DateTimeOffset?>(
            delivery,
            "ResolvedAt"));
    }

    [Fact]
    public async Task ResolveUnknown_Should_RejectPendingWithRetainedConfirmNotAcceptedAsConflict()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object run, object delivery) = await SeedConfirmedNotAcceptedAsync(store);
        SetInternalProperty(delivery, "Status", EmailDeliveryStatus.Pending);
        SetInternalProperty(run, "Status", EmailRunStatus.Processing);
        SetInternalProperty(run, "CompletedAt", null);

        ResolveUnknownDeliveryResult result =
            await store.TryResolveUnknownDeliveryAsync(
                new ResolveUnknownDeliveryCommand(
                    1,
                    2,
                    EmailUnknownResolutionAction.ConfirmNotAccepted,
                    externalRequestId: null,
                    "A0000001"),
                UtcNow);

        Assert.Equal(ResolveUnknownDeliveryOutcome.Rejected, result.Outcome);
        Assert.Equal("EMAIL_RESOLUTION_CONFLICT", result.ErrorCode);
    }

    [Fact]
    public async Task ManualRetry_Should_ReopenSelectedSnapshotOnceThenVersionRejectAfterClaim()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object run, object delivery) = await SeedConfirmedNotAcceptedAsync(store);
        DateTimeOffset lastAttemptAt = GetInternal<DateTimeOffset?>(
            delivery,
            "LastAttemptAt")!.Value;
        DateTimeOffset resolvedAt = GetInternal<DateTimeOffset?>(
            delivery,
            "ResolvedAt")!.Value;
        DateTimeOffset changedAt = new(
            2026,
            8,
            31,
            5,
            6,
            7,
            TimeSpan.Zero);
        var command = new RetryEmailDeliveryCommand(1, 2, "C1008267");

        RetryEmailDeliveryResult applied =
            await store.TryReopenManualRetryAsync(command, changedAt);
        RetryEmailDeliveryResult replay =
            await store.TryReopenManualRetryAsync(command, changedAt.AddMinutes(1));

        Assert.Equal(RetryEmailDeliveryOutcome.Applied, applied.Outcome);
        Assert.Equal(RetryEmailDeliveryOutcome.AlreadyMatched, replay.Outcome);
        Assert.Equal(EmailDeliveryStatus.Pending, GetInternal<EmailDeliveryStatus>(
            delivery,
            "Status"));
        Assert.Equal(EmailRunStatus.Processing, GetInternal<EmailRunStatus>(
            run,
            "Status"));
        Assert.Equal(2, GetInternal<int>(delivery, "AttemptCount"));
        Assert.Equal(lastAttemptAt, GetInternal<DateTimeOffset?>(
            delivery,
            "LastAttemptAt"));
        Assert.Equal("CONFIRM_NOT_ACCEPTED", GetInternal<string?>(
            delivery,
            "ResolutionAction"));
        Assert.Equal(resolvedAt, GetInternal<DateTimeOffset?>(
            delivery,
            "ResolvedAt"));
        Assert.Equal("owner@example.test", GetInternal<string?>(
            delivery,
            "EffectiveToEmail"));
        Assert.Equal("owner-1.xlsx", GetInternal<string?>(
            delivery,
            "AttachmentFileName"));
        Assert.Null(GetInternal<DateTimeOffset?>(delivery, "NextRetryAt"));
        Assert.Null(GetInternal<DateTimeOffset?>(delivery, "SubmitStartedAt"));
        Assert.Null(GetInternal<DateTimeOffset?>(delivery, "AcceptedAt"));
        Assert.Null(GetInternal<string?>(delivery, "ExternalRequestId"));
        Assert.Null(GetInternal<int?>(delivery, "LastHttpStatus"));
        Assert.Null(GetInternal<string?>(delivery, "LastErrorCode"));
        Assert.Null(GetInternal<string?>(delivery, "LastErrorMessage"));
        Assert.NotNull(await store.TryClaimNextDeliveryAsync(changedAt.AddMinutes(2)));

        RetryEmailDeliveryResult delayed =
            await store.TryReopenManualRetryAsync(command, changedAt.AddMinutes(3));
        Assert.Equal(RetryEmailDeliveryOutcome.Rejected, delayed.Outcome);
        Assert.Equal("EMAIL_DELIVERY_VERSION_CONFLICT", delayed.ErrorCode);
        Assert.Equal(3, GetInternal<int>(delivery, "AttemptCount"));
    }

    [Fact]
    public async Task ManualRetry_Should_RejectDelayedAttemptAfterLaterFailureRetainsResolution()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object run, object delivery) = await SeedConfirmedNotAcceptedAsync(store);
        var delayed = new RetryEmailDeliveryCommand(1, 2, "C1008267");
        Assert.Equal(
            RetryEmailDeliveryOutcome.Applied,
            (await store.TryReopenManualRetryAsync(delayed, UtcNow)).Outcome);
        Assert.NotNull(await store.TryClaimNextDeliveryAsync(UtcNow.AddMinutes(1)));
        SetInternalProperty(delivery, "Status", EmailDeliveryStatus.Failed);
        SetInternalProperty(run, "Status", EmailRunStatus.Failed);

        RetryEmailDeliveryResult result =
            await store.TryReopenManualRetryAsync(delayed, UtcNow.AddMinutes(2));

        Assert.Equal(RetryEmailDeliveryOutcome.Rejected, result.Outcome);
        Assert.Equal("EMAIL_DELIVERY_VERSION_CONFLICT", result.ErrorCode);
        Assert.Equal(3, GetInternal<int>(delivery, "AttemptCount"));
        Assert.Equal(EmailDeliveryStatus.Failed, GetInternal<EmailDeliveryStatus>(
            delivery,
            "Status"));
    }

    [Fact]
    public async Task ManualRetry_Replay_Should_NotMaskAnotherActiveRun()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object selectedRun, _) = await SeedConfirmedNotAcceptedAsync(store);
        var command = new RetryEmailDeliveryCommand(1, 2, "C1008267");
        Assert.Equal(
            RetryEmailDeliveryOutcome.Applied,
            (await store.TryReopenManualRetryAsync(command, UtcNow)).Outcome);
        SetInternalProperty(selectedRun, "Status", EmailRunStatus.Failed);
        Assert.Equal(
            EmailRunCreationOutcome.Created,
            (await store.TryCreateRunNowAsync(
                1,
                "A0000001",
                new EmailConfigurationSnapshot(
                    "ORACLE",
                    "API_EMAIL",
                    "SAFE_REDIRECT"))).Outcome);
        SetInternalProperty(selectedRun, "Status", EmailRunStatus.Processing);

        RetryEmailDeliveryResult result =
            await store.TryReopenManualRetryAsync(
                command,
                UtcNow.AddMinutes(1));

        Assert.Equal(RetryEmailDeliveryOutcome.ActiveRunConflict, result.Outcome);
        Assert.Equal(1, result.EmailRunId);
    }

    [Theory]
    [InlineData("NextRetryAt")]
    [InlineData("SubmitStartedAt")]
    [InlineData("AcceptedAt")]
    [InlineData("ExternalRequestId")]
    [InlineData("LastHttpStatus")]
    [InlineData("LastErrorCode")]
    [InlineData("LastErrorMessage")]
    [InlineData("RunCompletedAt")]
    [InlineData("RunLastErrorCode")]
    [InlineData("RunLastErrorMessage")]
    public async Task ManualRetry_Should_RejectCorruptPendingReplayShape(
        string field)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object run, object delivery) = await SeedConfirmedNotAcceptedAsync(store);
        var command = new RetryEmailDeliveryCommand(1, 2, "C1008267");
        Assert.Equal(
            RetryEmailDeliveryOutcome.Applied,
            (await store.TryReopenManualRetryAsync(command, UtcNow)).Outcome);
        switch (field)
        {
            case "NextRetryAt":
            case "SubmitStartedAt":
            case "AcceptedAt":
                SetInternalProperty(delivery, field, UtcNow);
                break;
            case "ExternalRequestId":
                SetInternalProperty(delivery, field, "forged-id");
                break;
            case "LastHttpStatus":
                SetInternalProperty(delivery, field, 500);
                break;
            case "LastErrorCode":
            case "LastErrorMessage":
                SetInternalProperty(delivery, field, "FORGED");
                break;
            case "RunCompletedAt":
                SetInternalProperty(run, "CompletedAt", UtcNow);
                break;
            case "RunLastErrorCode":
                SetInternalProperty(run, "LastErrorCode", "FORGED");
                break;
            case "RunLastErrorMessage":
                SetInternalProperty(run, "LastErrorMessage", "FORGED");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }

        RetryEmailDeliveryResult result =
            await store.TryReopenManualRetryAsync(
                command,
                UtcNow.AddMinutes(1));

        Assert.Equal(RetryEmailDeliveryOutcome.Rejected, result.Outcome);
    }

    [Theory]
    [InlineData(EmailRunStatus.Failed)]
    [InlineData(EmailRunStatus.Partial)]
    [InlineData(EmailRunStatus.Processing)]
    [InlineData(EmailRunStatus.ReviewRequired)]
    public async Task ManualRetry_Should_AllowEveryApprovedRunStatus(
        EmailRunStatus runStatus)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object run, object delivery) = await SeedConfirmedNotAcceptedAsync(store);
        SetInternalProperty(run, "Status", runStatus);
        if (runStatus is EmailRunStatus.Processing or EmailRunStatus.ReviewRequired)
        {
            SetInternalProperty(run, "CompletedAt", null);
        }

        RetryEmailDeliveryResult result =
            await store.TryReopenManualRetryAsync(
                new RetryEmailDeliveryCommand(1, 2, "C1008267"),
                UtcNow);

        Assert.Equal(RetryEmailDeliveryOutcome.Applied, result.Outcome);
        Assert.Equal(EmailDeliveryStatus.Pending, GetInternal<EmailDeliveryStatus>(
            delivery,
            "Status"));
        Assert.Equal(EmailRunStatus.Processing, GetInternal<EmailRunStatus>(
            run,
            "Status"));
    }

    [Theory]
    [InlineData(EmailRunStatus.Pending)]
    [InlineData(EmailRunStatus.Completed)]
    [InlineData(EmailRunStatus.Cancelled)]
    public async Task ManualRetry_Should_RejectInconsistentRunStatuses(
        EmailRunStatus runStatus)
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object run, object delivery) = await SeedConfirmedNotAcceptedAsync(store);
        SetInternalProperty(run, "Status", runStatus);

        RetryEmailDeliveryResult result =
            await store.TryReopenManualRetryAsync(
                new RetryEmailDeliveryCommand(1, 2, "C1008267"),
                UtcNow);

        Assert.Equal(RetryEmailDeliveryOutcome.Rejected, result.Outcome);
        Assert.Equal("EMAIL_MANUAL_RETRY_NOT_ALLOWED", result.ErrorCode);
        Assert.Equal(EmailDeliveryStatus.Failed, GetInternal<EmailDeliveryStatus>(
            delivery,
            "Status"));
    }

    [Fact]
    public async Task ManualRetry_Should_RejectFakeLimitAndOtherActiveRunWithoutMutation()
    {
        (_, DevelopmentEmailExecutionStore fakeStore) = CreateStore();
        await CreateProcessingRunAsync(
            fakeStore,
            OwnerDeliverySeed.Pending("C1000001"));
        object fakeRun = SingleInternal(fakeStore, "Runs");
        object fakeDelivery = SingleInternal(fakeStore, "Deliveries");
        SeedRetrySnapshot(fakeRun, fakeDelivery, attemptCount: 2);

        RetryEmailDeliveryResult fake =
            await fakeStore.TryReopenManualRetryAsync(
                new RetryEmailDeliveryCommand(1, 2, "C1008267"),
                UtcNow);
        Assert.Equal("EMAIL_MANUAL_RETRY_NOT_ALLOWED", fake.ErrorCode);

        (_, DevelopmentEmailExecutionStore limitStore) = CreateStore();
        (object limitRun, object limitDelivery) =
            await SeedConfirmedNotAcceptedAsync(limitStore);
        SetInternalProperty(limitDelivery, "AttemptCount", 999);
        RetryEmailDeliveryResult limit =
            await limitStore.TryReopenManualRetryAsync(
                new RetryEmailDeliveryCommand(1, 999, "C1008267"),
                UtcNow);
        Assert.Equal("EMAIL_ATTEMPT_LIMIT_EXCEEDED", limit.ErrorCode);

        (_, DevelopmentEmailExecutionStore conflictStore) = CreateStore();
        (object selectedRun, object selectedDelivery) =
            await SeedConfirmedNotAcceptedAsync(conflictStore);
        CreateEmailRunResult other = await conflictStore.TryCreateRunNowAsync(
            1,
            "A0000001",
            new EmailConfigurationSnapshot(
                "ORACLE",
                "API_EMAIL",
                "SAFE_REDIRECT"));
        Assert.Equal(EmailRunCreationOutcome.Created, other.Outcome);

        RetryEmailDeliveryResult conflict =
            await conflictStore.TryReopenManualRetryAsync(
                new RetryEmailDeliveryCommand(1, 2, "C1008267"),
                UtcNow);
        Assert.Equal(RetryEmailDeliveryOutcome.ActiveRunConflict, conflict.Outcome);
        Assert.Equal(EmailDeliveryStatus.Failed, GetInternal<EmailDeliveryStatus>(
            selectedDelivery,
            "Status"));
        Assert.Equal(EmailRunStatus.Failed, GetInternal<EmailRunStatus>(
            selectedRun,
            "Status"));
    }

    [Fact]
    public async Task ManualRetry_Should_MutateOnlySelectedDelivery()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object run, object firstDelivery) = await SeedUnknownAsync(
            store,
            OwnerDeliverySeed.Pending("C1000001"),
            OwnerDeliverySeed.Pending("C1000002"));
        object secondDelivery = ReadInternal(store, "Deliveries").Last();
        SeedRetrySnapshot(run, firstDelivery, attemptCount: 2);
        SetInternalProperty(secondDelivery, "Status", EmailDeliveryStatus.Accepted);
        SetInternalProperty(secondDelivery, "AttemptCount", 9);
        SetInternalProperty(secondDelivery, "ExternalRequestId", "keep-me");

        RetryEmailDeliveryResult result =
            await store.TryReopenManualRetryAsync(
                new RetryEmailDeliveryCommand(1, 2, "C1008267"),
                UtcNow);

        Assert.Equal(RetryEmailDeliveryOutcome.Applied, result.Outcome);
        Assert.Equal(EmailDeliveryStatus.Pending, GetInternal<EmailDeliveryStatus>(
            firstDelivery,
            "Status"));
        Assert.Equal(EmailDeliveryStatus.Accepted, GetInternal<EmailDeliveryStatus>(
            secondDelivery,
            "Status"));
        Assert.Equal(9, GetInternal<int>(secondDelivery, "AttemptCount"));
        Assert.Equal("keep-me", GetInternal<string?>(
            secondDelivery,
            "ExternalRequestId"));
    }

    [Fact]
    public async Task ManagementReads_Should_OrderCountDetachAndHideArtifactPaths()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        var apiSnapshot = new EmailConfigurationSnapshot(
            "ORACLE",
            "API_EMAIL",
            "SAFE_REDIRECT");
        long runId = await CreateProcessingRunAsync(
            store,
            apiSnapshot,
            OwnerDeliverySeed.Pending("C1000001"),
            OwnerDeliverySeed.Pending("C1000002"),
            OwnerDeliverySeed.Pending("C1000003"),
            OwnerDeliverySeed.Pending("C1000004"),
            OwnerDeliverySeed.Pending("C1000005"),
            OwnerDeliverySeed.Pending("C1000006"));
        object run = SingleInternal(store, "Runs");
        object[] deliveries = ReadInternal(store, "Deliveries").ToArray();
        EmailDeliveryStatus[] statuses =
        [
            EmailDeliveryStatus.Pending,
            EmailDeliveryStatus.Accepted,
            EmailDeliveryStatus.Simulated,
            EmailDeliveryStatus.Failed,
            EmailDeliveryStatus.Unknown,
            EmailDeliveryStatus.Cancelled
        ];
        for (int index = 0; index < deliveries.Length; index++)
        {
            SetInternalProperty(deliveries[index], "Status", statuses[index]);
        }

        EmailArtifactMetadata workbook = Artifact("owner-1.xlsx");
        EmailArtifactMetadata zip = Artifact("run-1.zip");
        SetStoredArtifact(deliveries[0], workbook);
        SetInternalProperty(run, "ZipFileName", zip.FileName);
        SetInternalProperty(run, "ZipStoragePath", zip.StoragePath);

        IEmailManagementReader reader = store;
        EmailApplicationOverview application = (await reader
            .GetEmailApplicationOverviewsAsync()).Single(item =>
                item.ApplicationId == 1);
        EmailRunSummary summary = Assert.Single(
            await reader.GetRecentEmailRunsAsync(1, 1));
        EmailRunDetail detail = Assert.IsType<EmailRunDetail>(
            await reader.GetEmailRunDetailAsync(runId));

        Assert.Equal(runId, application.ActiveEmailRunId);
        Assert.Equal(EmailRunStatus.Processing, application.ActiveRunStatus);
        Assert.Equal(6, summary.TotalCount);
        Assert.Equal(1, summary.InFlightCount);
        Assert.Equal(1, summary.AcceptedCount);
        Assert.Equal(1, summary.SimulatedCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(1, summary.UnknownCount);
        Assert.Equal(1, summary.CancelledCount);
        Assert.Equal(
            deliveries.Select(item => GetInternal<long>(item, "EmailDeliveryId")),
            detail.Deliveries.Select(item => item.EmailDeliveryId));
        Assert.Equal(zip, await reader.GetEmailRunZipArtifactAsync(runId));
        Assert.Equal(workbook, await reader.GetEmailDeliveryWorkbookArtifactAsync(
            runId,
            1));
        Assert.Null(await reader.GetEmailDeliveryWorkbookArtifactAsync(
            runId + 999,
            1));

        SetInternalProperty(run, "ZipStoragePath", null);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.GetEmailRunZipArtifactAsync(runId));
    }

    [Fact]
    public async Task ManagementReads_Should_ApplyRecentRunLimitWithDescendingTieOrdering()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        for (int index = 0; index < 101; index++)
        {
            CreateEmailRunResult created = await store.TryCreateRunNowAsync(
                1,
                "C1008267",
                SafeSnapshot);
            Assert.Equal(EmailRunCreationOutcome.Created, created.Outcome);
            object run = ReadInternal(store, "Runs").Last();
            SetInternalProperty(run, "Status", EmailRunStatus.Completed);
        }

        IReadOnlyList<EmailRunSummary> recent =
            await ((IEmailManagementReader)store).GetRecentEmailRunsAsync(1, 100);

        Assert.Equal(100, recent.Count);
        Assert.Equal(101, recent.First().EmailRunId);
        Assert.Equal(2, recent.Last().EmailRunId);
    }

    [Fact]
    public async Task ManagementReads_Should_RejectInvalidDeliveryDetailAndPartialWorkbookMetadata()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        (object run, object delivery) = await SeedUnknownAsync(store);
        long runId = GetInternal<long>(run, "EmailRunId");
        SetInternalProperty(delivery, "ResolutionAction", "BROKEN");

        IEmailManagementReader reader = store;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.GetEmailRunDetailAsync(runId));

        SetInternalProperty(delivery, "ResolutionAction", null);
        SetInternalProperty(delivery, "AttachmentFileName", "owner-1.xlsx");
        SetInternalProperty(delivery, "AttachmentStoragePath", null);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.GetEmailDeliveryWorkbookArtifactAsync(runId, 1));
    }

    [Fact]
    public async Task StoreReads_Should_ReturnDetachedImmutableRows()
    {
        (_, DevelopmentEmailExecutionStore store) = CreateStore();
        await store.SaveScheduleAsync(new SaveEmailScheduleCommand(
            1,
            Bangkok(2027, 3, 8, 9),
            "C1008267"));
        await store.TryCreateRunNowAsync(1, "C1008267", SafeSnapshot);

        EmailScheduleRow firstSchedule = Assert.Single(
            await store.GetSchedulesAsync());
        EmailScheduleRow secondSchedule = Assert.Single(
            await store.GetSchedulesAsync());
        PendingEmailRun firstRun = Assert.Single(await store.GetPendingRunsAsync(10));
        PendingEmailRun secondRun = Assert.Single(await store.GetPendingRunsAsync(10));

        Assert.NotSame(firstSchedule, secondSchedule);
        Assert.Equal(firstSchedule, secondSchedule);
        Assert.NotSame(firstRun, secondRun);
        Assert.Equal(firstRun, secondRun);
    }

    [Fact]
    public async Task StoreOperations_Should_RespectPreCancelledTokenWithoutMutation()
    {
        (DevelopmentRoleVData data, DevelopmentEmailExecutionStore store) =
            CreateStore();
        DateTimeOffset originalNextRunAt = Bangkok(2027, 3, 8, 9);
        AdministrationResult saved = await store.SaveScheduleAsync(
            new SaveEmailScheduleCommand(
                1,
                originalNextRunAt,
                "C1008267"));
        long runId = (await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            SafeSnapshot)).EmailRunId!.Value;
        int historyBefore = data.ChangeHistory.Count;
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveScheduleAsync(
                new SaveEmailScheduleCommand(
                    1,
                    Bangkok(2028, 4, 9, 10),
                    "C1008267"),
                source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SetScheduleActiveAsync(
                new SetEmailScheduleActiveCommand(
                    1,
                    saved.EntityId!.Value,
                    true,
                    "C1008267"),
                source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryCreateScheduledRunAsync(
                Bangkok(2027, 3, 8, 10),
                SafeSnapshot,
                source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryCreateRunNowAsync(
                1,
                "C1008267",
                SafeSnapshot,
                source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryStartRunAsync(
                runId,
                [OwnerDeliverySeed.Pending("00090775")],
                source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.MarkStuckSubmittingUnknownAsync(
                Bangkok(2027, 3, 8, 9),
                10,
                source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryClaimNextDeliveryAsync(
                Bangkok(2027, 3, 8, 9),
                source.Token));
        var lease = new EmailDeliveryAttemptLease(
            1,
            runId,
            1,
            Bangkok(2027, 3, 8, 9));
        EmailArtifactMetadata artifact = Artifact("owner-1.xlsx");
        var expectedManifest = new EmailRunArtifactManifest(
            runId,
            1,
            EmailRunStatus.Processing,
            zipArtifact: null,
            [
                new EmailRunArtifactDeliveryEntry(
                    1,
                    EmailDeliveryStatus.Preparing,
                    1,
                    lastErrorCode: null,
                    artifact)
            ]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryPersistDeliveryArtifactAsync(
                lease,
                artifact,
                source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.GetRunArtifactManifestAsync(runId, source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.TryPersistRunZipArtifactAsync(
                expectedManifest,
                Artifact("run.zip"),
                source.Token));

        EmailScheduleRow schedule = Assert.Single(await store.GetSchedulesAsync());
        Assert.Equal(originalNextRunAt, schedule.NextRunAt);
        Assert.False(schedule.IsActive);
        Assert.Equal(historyBefore, data.ChangeHistory.Count);
        Assert.Single(ReadInternal(store, "Runs"));
        Assert.Equal(
            EmailRunStatus.Pending,
            GetInternal<EmailRunStatus>(SingleInternal(store, "Runs"), "Status"));
        Assert.Empty(ReadInternal(store, "Deliveries"));
    }

    private static async Task<int> SaveAndActivateAsync(
        DevelopmentEmailExecutionStore store,
        DateTimeOffset nextRunAt,
        int applicationId = 1)
    {
        AdministrationResult saved = await store.SaveScheduleAsync(
            new SaveEmailScheduleCommand(
                applicationId,
                nextRunAt,
                "C1008267"));
        Assert.True(saved.Succeeded);
        AdministrationResult activated = await store.SetScheduleActiveAsync(
            new SetEmailScheduleActiveCommand(
                applicationId,
                saved.EntityId!.Value,
                true,
                "C1008267"));
        Assert.True(activated.Succeeded);
        return saved.EntityId.Value;
    }

    private static async Task<long> CreateProcessingRunAsync(
        DevelopmentEmailExecutionStore store,
        params OwnerDeliverySeed[] seeds) =>
        await CreateProcessingRunAsync(store, SafeSnapshot, seeds);

    private static async Task<long> CreateProcessingRunAsync(
        DevelopmentEmailExecutionStore store,
        EmailConfigurationSnapshot snapshot,
        params OwnerDeliverySeed[] seeds)
    {
        CreateEmailRunResult created = await store.TryCreateRunNowAsync(
            1,
            "C1008267",
            snapshot);
        Assert.Equal(EmailRunCreationOutcome.Created, created.Outcome);
        StartEmailRunResult started = await store.TryStartRunAsync(
            created.EmailRunId!.Value,
            seeds);
        Assert.Equal(StartEmailRunOutcome.Started, started.Outcome);
        return created.EmailRunId.Value;
    }

    private static async Task<(object Run, object Delivery)> SeedUnknownAsync(
        DevelopmentEmailExecutionStore store,
        params OwnerDeliverySeed[] seeds)
    {
        OwnerDeliverySeed[] effectiveSeeds = seeds.Length == 0
            ? [OwnerDeliverySeed.Pending("C1000001")]
            : seeds;
        var snapshot = new EmailConfigurationSnapshot(
            "ORACLE",
            "API_EMAIL",
            "SAFE_REDIRECT");
        await CreateProcessingRunAsync(store, snapshot, effectiveSeeds);
        object run = SingleInternal(store, "Runs");
        object[] deliveries = ReadInternal(store, "Deliveries").ToArray();
        foreach (object delivery in deliveries)
        {
            SetInternalProperty(delivery, "Status", EmailDeliveryStatus.Unknown);
            SetInternalProperty(delivery, "AttemptCount", 2);
            SetInternalProperty(delivery, "LastAttemptAt", UtcNow.AddMinutes(-2));
            SetInternalProperty(
                delivery,
                "SubmitStartedAt",
                UtcNow.AddMinutes(-1));
            SetInternalProperty(delivery, "LastHttpStatus", 504);
            SetInternalProperty(
                delivery,
                "LastErrorCode",
                "API_EMAIL_TIMEOUT");
            SetInternalProperty(
                delivery,
                "LastErrorMessage",
                "provider result uncertain");
            SetInternalProperty(
                delivery,
                "EffectiveToEmail",
                "owner@example.test");
            SetInternalProperty(delivery, "EmailSubject", "Annual review");
            SetInternalProperty(delivery, "EmailBody", "Review roles.");
            SetStoredArtifact(delivery, Artifact($"owner-{GetInternal<long>(
                delivery,
                "EmailDeliveryId")}.xlsx"));
        }

        SetInternalProperty(run, "Status", EmailRunStatus.ReviewRequired);
        SetInternalProperty(run, "LastErrorCode", "STALE_RUN_ERROR");
        SetInternalProperty(run, "LastErrorMessage", "stale run message");
        return (run, deliveries[0]);
    }

    private static async Task<(object Run, object Delivery)>
        SeedConfirmedNotAcceptedAsync(DevelopmentEmailExecutionStore store)
    {
        (object run, object delivery) = await SeedUnknownAsync(store);
        SeedRetrySnapshot(run, delivery, attemptCount: 2);
        return (run, delivery);
    }

    private static void SeedRetrySnapshot(
        object run,
        object delivery,
        int attemptCount)
    {
        SetInternalProperty(delivery, "Status", EmailDeliveryStatus.Failed);
        SetInternalProperty(delivery, "AttemptCount", attemptCount);
        SetInternalProperty(delivery, "LastAttemptAt", UtcNow.AddMinutes(-2));
        SetInternalProperty(delivery, "NextRetryAt", UtcNow.AddMinutes(10));
        SetInternalProperty(delivery, "SubmitStartedAt", UtcNow.AddMinutes(-1));
        SetInternalProperty(delivery, "AcceptedAt", UtcNow);
        SetInternalProperty(delivery, "ExternalRequestId", "old-provider-id");
        SetInternalProperty(delivery, "LastHttpStatus", 504);
        SetInternalProperty(delivery, "LastErrorCode", "API_EMAIL_TIMEOUT");
        SetInternalProperty(delivery, "LastErrorMessage", "uncertain");
        SetInternalProperty(
            delivery,
            "EffectiveToEmail",
            "owner@example.test");
        SetInternalProperty(delivery, "EmailSubject", "Annual review");
        SetInternalProperty(delivery, "EmailBody", "Review roles.");
        SetStoredArtifact(delivery, Artifact("owner-1.xlsx"));
        SetInternalProperty(
            delivery,
            "ResolutionAction",
            "CONFIRM_NOT_ACCEPTED");
        SetInternalProperty(
            delivery,
            "ResolvedByEmployeeNo",
            "C1008267");
        SetInternalProperty(delivery, "ResolvedAt", UtcNow.AddMinutes(1));
        SetInternalProperty(run, "Status", EmailRunStatus.Failed);
        SetInternalProperty(run, "CompletedAt", UtcNow.AddMinutes(1));
        SetInternalProperty(run, "LastErrorCode", "STALE_RUN_ERROR");
        SetInternalProperty(run, "LastErrorMessage", "stale run message");
    }

    private static void DeactivateApplicationUnderSharedGate(
        DevelopmentRoleVData data,
        int applicationId)
    {
        lock (data.EmailExecutionGate)
        {
            data.Applications.Single(application =>
                application.ApplicationId == applicationId).Deactivate();
        }
    }

    private static (
        DevelopmentRoleVData Data,
        DevelopmentEmailExecutionStore Store) CreateStore()
    {
        var data = new DevelopmentRoleVData();
        var timeProvider = new StubTimeProvider(
            new DateTimeOffset(2026, 8, 30, 5, 0, 0, TimeSpan.Zero));
        return (data, new DevelopmentEmailExecutionStore(
            data,
            "C2001234",
            timeProvider));
    }

    private static DateTimeOffset Bangkok(
        int year,
        int month,
        int day,
        int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.FromHours(7));

    private static EmailArtifactMetadata Artifact(string fileName) =>
        new(
            fileName,
            Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "role-validation-artifacts",
                fileName)));

    private static EmailDeliveryAttemptLease[] EveryLeaseMismatch(
        EmailDeliveryAttemptLease lease) =>
    [
        new(
            lease.EmailDeliveryId + 999,
            lease.EmailRunId,
            lease.AttemptCount,
            lease.LastAttemptAt),
        new(
            lease.EmailDeliveryId,
            lease.EmailRunId + 999,
            lease.AttemptCount,
            lease.LastAttemptAt),
        new(
            lease.EmailDeliveryId,
            lease.EmailRunId,
            checked(lease.AttemptCount + 1),
            lease.LastAttemptAt),
        new(
            lease.EmailDeliveryId,
            lease.EmailRunId,
            lease.AttemptCount,
            lease.LastAttemptAt.AddTicks(10))
    ];

    private static void SetStoredArtifact(
        object target,
        EmailArtifactMetadata artifact)
    {
        SetInternalProperty(target, "AttachmentFileName", artifact.FileName);
        SetInternalProperty(target, "AttachmentStoragePath", artifact.StoragePath);
    }

    private static IList ReadInternalList(
        object target,
        string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Internal property {propertyName} was not found.");
        return (IList)property.GetValue(target)!;
    }

    private static IEnumerable<object> ReadInternal(
        object target,
        string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Internal property {propertyName} was not found.");
        return ((IEnumerable)property.GetValue(target)!).Cast<object>();
    }

    private static object SingleInternal(object target, string propertyName) =>
        Assert.Single(ReadInternal(target, propertyName));

    private static T GetInternal<T>(object target, string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Property {propertyName} was not found.");
        return (T)property.GetValue(target)!;
    }

    private static void SetInternalProperty(
        object target,
        string propertyName,
        object? value)
    {
        PropertyInfo property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Property {propertyName} was not found.");
        property.SetValue(target, value);
    }

    private static void SetInternalBackingField(
        object target,
        string propertyName,
        object? value)
    {
        FieldInfo field = target.GetType().GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Backing field for {propertyName} was not found.");
        field.SetValue(target, value);
    }

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
