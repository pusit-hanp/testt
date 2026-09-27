using System.Data.Common;
using System.Globalization;
using RoleValidation.Application.Users;
using RoleValidation.Infrastructure.ApplicationUsers;
using RoleValidation.Infrastructure.Database;

namespace RoleValidation.Infrastructure.Tests.ApplicationUsers;

public sealed class PullListApplicationUserProviderTests
{
    [Fact]
    public void Provider_Should_UseMaterialConnectionAndStableApplicationCode()
    {
        var provider = new PullListApplicationUserProvider(
            new StubMaterialOracleConnectionFactory());

        Assert.Equal("PULL_LIST", provider.ApplicationCode);
    }

    [Fact]
    public void CreateCommand_Should_ReadMaterialWithoutMasterEmployeeFiltering()
    {
        string sql = PullListApplicationUserProvider
            .CreateCommand()
            .CommandText;

        Assert.Contains("MATERIAL.AUTHORIZE_USER", sql);
        Assert.Contains("a.STATUS = 'A'", sql);
        Assert.DoesNotContain("MASTER_EMPLOYEE", sql);
        Assert.DoesNotContain("EMP_STATUS", sql);
        Assert.DoesNotContain("USER_GROUP <> 'PRO'", sql);
    }

    [Theory]
    [InlineData("STO", "Store")]
    [InlineData("ADM", "Admin Store")]
    [InlineData("PRC", "PRC")]
    [InlineData("PRO", "Production")]
    [InlineData("MGR", "MGR")]
    public void Map_Should_PreserveSourceKeyAndConfirmedDisplay(
        string sourceKey,
        string sourceDisplay)
    {
        string employeeNo = Random.Shared.Next(10_000_000, 100_000_000)
            .ToString(CultureInfo.InvariantCulture);
        var row = new PullListApplicationUserRow
        {
            EmployeeNo = employeeNo,
            UserName = $"th{employeeNo}",
            SourceRoleKey = sourceKey,
            LastLoginAt = new DateTime(2026, 8, 20)
        };

        ApplicationUserRecord result =
            PullListApplicationUserProvider.Map(row);

        Assert.Equal(sourceKey, result.SourceRoleKey);
        Assert.Equal(sourceDisplay, result.SourceRoleDisplayName);
        Assert.Equal(row.EmployeeNo, result.EmployeeNo);
        Assert.Equal(row.UserName, result.UserName);
        Assert.Equal(row.LastLoginAt, result.LastLoginAt);
    }

    [Fact]
    public void Map_Should_KeepUnknownSourceRoleForSharedMappingResolution()
    {
        string employeeNo = Random.Shared.Next(10_000_000, 100_000_000)
            .ToString(CultureInfo.InvariantCulture);
        var row = new PullListApplicationUserRow
        {
            EmployeeNo = employeeNo,
            UserName = $"th{employeeNo}",
            SourceRoleKey = "NEW_ROLE"
        };

        ApplicationUserRecord result =
            PullListApplicationUserProvider.Map(row);

        Assert.Equal("NEW_ROLE", result.SourceRoleKey);
        Assert.Null(result.SourceRoleDisplayName);
    }

    private sealed class StubMaterialOracleConnectionFactory
        : IMaterialOracleConnectionFactory
    {
        public DbConnection CreateConnection()
        {
            throw new NotSupportedException();
        }
    }
}
