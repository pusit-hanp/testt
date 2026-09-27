using System.Data.Common;
using RoleValidation.Application.Users;
using RoleValidation.Infrastructure.ApplicationUsers;
using RoleValidation.Infrastructure.Database;

namespace RoleValidation.Infrastructure.Tests.ApplicationUsers;

public sealed class OpenMarketApplicationUserProviderTests
{
    [Fact]
    public void Provider_Should_UseMasterConnectionAndStableApplicationCode()
    {
        var provider = new OpenMarketApplicationUserProvider(
            new StubMasterOracleConnectionFactory());

        Assert.Equal("OPM", provider.ApplicationCode);
    }

    [Fact]
    public void CreateCommand_Should_KeepLegacyAccountBoundaryOnly()
    {
        string sql = OpenMarketApplicationUserProvider
            .CreateCommand()
            .CommandText;

        Assert.Contains("SYSTEM_USER_INFO", sql);
        Assert.Contains("SYSTEM_AUTHUSER", sql);
        Assert.Contains("SYSTEM_AUTHITEM", sql);
        Assert.Contains("SUBSTR(a.USR_ITEM, 1, 3) = 'OPM'", sql);
        Assert.Contains("u.USER_STATUS = 'A'", sql);
        Assert.DoesNotContain("MASTER_EMPLOYEE", sql);
        Assert.DoesNotContain("EMP_STATUS", sql);
        Assert.Contains("ORDER BY UserName, SourceRoleKey", sql);
    }

    [Theory]
    [InlineData("OPM0001", "RFQ Record")]
    [InlineData("OPM0003", "Admin")]
    [InlineData("OPM0002", "Master Management")]
    public void Map_Should_PreserveItemKey(string key, string display)
    {
        string expectedUserName = $@"asia\test.{Guid.NewGuid():N}";
        var row = new OpenMarketApplicationUserRow
        {
            EmployeeNo = "26008200",
            UserName = expectedUserName,
            SourceRoleKey = key,
            SourceRoleDisplayName = display,
            LastLoginAt = new DateTime(2026, 8, 20)
        };

        ApplicationUserRecord result =
            OpenMarketApplicationUserProvider.Map(row);

        Assert.Equal("26008200", result.EmployeeNo);
        Assert.Equal(expectedUserName, result.UserName);
        Assert.Equal(key, result.SourceRoleKey);
        Assert.Equal(display, result.SourceRoleDisplayName);
        Assert.Equal(row.LastLoginAt, result.LastLoginAt);
    }

    private sealed class StubMasterOracleConnectionFactory
        : IMasterOracleConnectionFactory
    {
        public DbConnection CreateConnection() =>
            throw new NotSupportedException();
    }
}
