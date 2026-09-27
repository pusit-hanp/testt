using System.Data.Common;
using RoleValidation.Application.Users;
using RoleValidation.Infrastructure.ApplicationUsers;
using RoleValidation.Infrastructure.Database;

namespace RoleValidation.Infrastructure.Tests.ApplicationUsers;

public sealed class MfmApplicationUserProviderTests
{
    [Fact]
    public void Provider_Should_UseMasterConnectionAndStableApplicationCode()
    {
        var provider = new MfmApplicationUserProvider(
            new StubMasterOracleConnectionFactory());

        Assert.Equal("MFM", provider.ApplicationCode);
    }

    [Fact]
    public void CreateCommand_Should_LoadEveryCusItemWithoutEmployeeFiltering()
    {
        string sql = MfmApplicationUserProvider.CreateCommand().CommandText;

        Assert.Contains("SYSTEM_AUTHUSER", sql);
        Assert.Contains("SYSTEM_AUTHITEM", sql);
        Assert.Contains("a.USR_ITEM LIKE 'CUS%'", sql);
        Assert.DoesNotContain("CASE", sql);
        Assert.DoesNotContain("MASTER_EMPLOYEE", sql);
        Assert.DoesNotContain("EMP_STATUS", sql);
        Assert.DoesNotContain("USER_STATUS", sql);
        Assert.Contains("ORDER BY UserName, SourceRoleKey", sql);
    }

    [Fact]
    public void Map_Should_PreserveCusItem()
    {
        string expectedUserName = $@"asia\test.{Guid.NewGuid():N}";
        var row = new MfmApplicationUserRow
        {
            EmployeeNo = "26008200",
            UserName = expectedUserName,
            SourceRoleKey = "CUSCI",
            SourceRoleDisplayName = "Customer Item",
            LastLoginAt = new DateTime(2026, 8, 20)
        };

        ApplicationUserRecord result = MfmApplicationUserProvider.Map(row);

        Assert.Equal("26008200", result.EmployeeNo);
        Assert.Equal(expectedUserName, result.UserName);
        Assert.Equal("CUSCI", result.SourceRoleKey);
        Assert.Equal("Customer Item", result.SourceRoleDisplayName);
        Assert.Equal(row.LastLoginAt, result.LastLoginAt);
    }

    private sealed class StubMasterOracleConnectionFactory
        : IMasterOracleConnectionFactory
    {
        public DbConnection CreateConnection() =>
            throw new NotSupportedException();
    }
}
