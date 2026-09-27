using System.Data.Common;
using RoleValidation.Application.Users;
using RoleValidation.Infrastructure.ApplicationUsers;
using RoleValidation.Infrastructure.Database;

namespace RoleValidation.Infrastructure.Tests.ApplicationUsers;

public sealed class EvaApplicationUserProviderTests
{
    [Fact]
    public void Provider_Should_UseMasterConnectionAndStableApplicationCode()
    {
        var provider = new EvaApplicationUserProvider(
            new StubMasterOracleConnectionFactory());

        Assert.Equal("EVA", provider.ApplicationCode);
    }

    [Fact]
    public void CreateCommand_Should_ReadFullEvaCatalogWithoutEmployeeFiltering()
    {
        string sql = EvaApplicationUserProvider.CreateCommand().CommandText;

        Assert.Contains("SYSTEM_AUTHUSERROLE", sql);
        Assert.Contains("SYSTEM_AUTHROLE", sql);
        Assert.Contains("r.ROL_PROJECT = 'EVA'", sql);
        Assert.DoesNotContain("USR_ROLE IN", sql);
        Assert.DoesNotContain("MASTER_EMPLOYEE", sql);
        Assert.DoesNotContain("EMP_STATUS", sql);
        Assert.DoesNotContain("USER_STATUS", sql);
        Assert.Contains("ORDER BY UserName, SourceRoleKey", sql);
    }

    [Theory]
    [InlineData("EVA02", "ADMIN")]
    [InlineData("ADMIN", "ADMIN")]
    [InlineData("USER", "USER")]
    public void Map_Should_PreserveSourceKey(string key, string display)
    {
        string expectedUserName = $@"asia\test.{Guid.NewGuid():N}";
        var row = new EvaApplicationUserRow
        {
            EmployeeNo = "26008200",
            UserName = expectedUserName,
            SourceRoleKey = key,
            SourceRoleDisplayName = display,
            LastLoginAt = new DateTime(2026, 8, 20)
        };

        ApplicationUserRecord result = EvaApplicationUserProvider.Map(row);

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
