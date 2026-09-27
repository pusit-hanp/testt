using RoleValidation.Application.Exports;
using RoleValidation.Application.RoleValidation;
using RoleValidation.Core.Features.RoleValidation;
using RoleValidation.Core.Features.SourceMappings;

namespace RoleValidation.Application.Tests.Exports;

public sealed class ApplicationUserExportDeduplicatorTests
{
    private static readonly string DefaultUserName = $"test.{Guid.NewGuid():N}";

    [Fact]
    public void Deduplicate_Should_CollapseSourceRolesMappedToSameCanonicalRole()
    {
        ApplicationUserView[] users =
        [
            Create("PRC", employeeNo: "00234053", roleId: 20),
            Create("PRO", employeeNo: "00234053", roleId: 20),
            Create("MGR", employeeNo: "00234053", roleId: 20)
        ];

        IReadOnlyList<ApplicationUserView> result =
            ApplicationUserExportDeduplicator.Deduplicate(users);

        Assert.Same(users[0], Assert.Single(result));
    }

    [Fact]
    public void Deduplicate_Should_PreserveRows_WhenAnyExportedColumnDiffers()
    {
        ApplicationUserView baseline = Create(
            "PRC",
            employeeNo: "00234053",
            roleId: 20,
            lastLoginAt: new DateTime(2026, 8, 20));
        ApplicationUserView[] variations =
        [
            Create(
                "PRC",
                employeeNo: "00234053",
                roleId: 20,
                userName: $"{DefaultUserName}.old",
                lastLoginAt: new DateTime(2026, 8, 20)),
            Create(
                "PRC",
                employeeNo: "00234053",
                roleId: 20,
                employeeName: "Jane S.",
                lastLoginAt: new DateTime(2026, 8, 20)),
            Create(
                "PRC",
                employeeNo: "00234054",
                roleId: 20,
                lastLoginAt: new DateTime(2026, 8, 20)),
            Create(
                "PRC",
                employeeNo: "00234053",
                roleId: 20,
                position: "Senior Buyer",
                lastLoginAt: new DateTime(2026, 8, 20)),
            Create(
                "PRC",
                employeeNo: "00234053",
                roleId: 20,
                email: "jane.smith.old@example.test",
                lastLoginAt: new DateTime(2026, 8, 20)),
            Create(
                "PRC",
                employeeNo: "00234053",
                roleId: 21,
                roleName: "Store",
                lastLoginAt: new DateTime(2026, 8, 20)),
            Create(
                "PRC",
                employeeNo: "00234053",
                roleId: 20,
                department: "SCM",
                lastLoginAt: new DateTime(2026, 8, 20)),
            Create(
                "PRC",
                employeeNo: "00234053",
                roleId: 20,
                lastLoginAt: new DateTime(2026, 8, 19))
        ];

        foreach (ApplicationUserView variation in variations)
        {
            IReadOnlyList<ApplicationUserView> result =
                ApplicationUserExportDeduplicator.Deduplicate(
                    [baseline, variation]);

            Assert.Equal(2, result.Count);
        }
    }

    [Fact]
    public void Deduplicate_Should_PreserveDistinctRolesAndEveryUnmappedRow()
    {
        ApplicationUserView[] users =
        [
            Create("PRC", employeeNo: "00234053", roleId: 20),
            Create(
                "ADM",
                employeeNo: "00234053",
                roleId: 21,
                roleName: "Admin Store"),
            Create("UNKNOWN-1", employeeNo: "00234053", roleId: null),
            Create("UNKNOWN-2", employeeNo: "00234053", roleId: null)
        ];

        IReadOnlyList<ApplicationUserView> result =
            ApplicationUserExportDeduplicator.Deduplicate(users);

        Assert.Equal(users, result);
    }

    [Fact]
    public void Deduplicate_Should_PreserveMfmRowsWithDifferentCustomerCodes()
    {
        ApplicationUserView[] users =
        [
            Create(
                "CUSCI",
                employeeNo: "00234053",
                roleId: 20,
                roleName: "Buyer-User",
                sourceRoleDisplayName: "CI"),
            Create(
                "CUS0C",
                employeeNo: "00234053",
                roleId: 20,
                roleName: "Buyer-User",
                sourceRoleDisplayName: "0C")
        ];

        IReadOnlyList<ApplicationUserView> result =
            ApplicationUserExportDeduplicator.Deduplicate(
                users,
                ApplicationUserExportProfile.ForApplication("MFM"));

        Assert.Equal(users, result);
    }

    [Fact]
    public void Deduplicate_Should_CollapseIcproMenuRowsForCurrentWorkbook()
    {
        ApplicationUserView[] users =
        [
            Create(
                "ADMINISTRATOR",
                employeeNo: "",
                roleId: 20,
                roleName: "User Admin",
                sourceRoleDisplayName: "User Maintenance"),
            Create(
                "ADMINISTRATOR",
                employeeNo: "",
                roleId: 20,
                roleName: "User Admin",
                sourceRoleDisplayName: "Role Maintenance")
        ];

        IReadOnlyList<ApplicationUserView> result =
            ApplicationUserExportDeduplicator.Deduplicate(
                users,
                ApplicationUserExportProfile.ForApplication("ICPLB"));

        Assert.Same(users[0], Assert.Single(result));
    }

    private static ApplicationUserView Create(
        string sourceRoleKey,
        string employeeNo,
        int? roleId,
        string? userName = null,
        string employeeName = "Jane Smith",
        string email = "jane.smith@example.test",
        string position = "Buyer",
        string department = "Purchasing",
        string? roleName = null,
        string? sourceRoleDisplayName = null,
        DateTime? lastLoginAt = null)
    {
        bool resolved = roleId.HasValue;

        return new ApplicationUserView(
            employeeNo,
            employeeName,
            employeeStatus: new EmployeeStatus("A"),
            sourceRoleKey,
            sourceRoleDisplayName: sourceRoleDisplayName ?? sourceRoleKey,
            roleId,
            roleName: resolved ? roleName ?? "Production" : null,
            resolutionType: resolved
                ? SourceRoleResolutionType.Resolved
                : SourceRoleResolutionType.NotMapped,
            userName ?? DefaultUserName,
            email,
            position,
            department,
            lastLoginAt);
    }
}
