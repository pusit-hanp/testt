using System.Data.Common;
using RoleValidation.Application.Users;
using RoleValidation.Infrastructure.ApplicationUsers;
using RoleValidation.Infrastructure.Database;

namespace RoleValidation.Infrastructure.Tests.ApplicationUsers;

public sealed class IdmApplicationUserProviderTests
{
    [Fact]
    public void Provider_Should_UseMasterConnectionAndStableApplicationCode()
    {
        var provider = new IdmApplicationUserProvider(
            new StubMasterOracleConnectionFactory());

        Assert.Equal("IDM", provider.ApplicationCode);
    }

    [Fact]
    public void CreateCommand_Should_ReadFullCatalogWithoutEmployeeFiltering()
    {
        string sql = IdmApplicationUserProvider.CreateCommand().CommandText;

        Assert.Contains("SYSTEM_AUTHUSERROLE", sql);
        Assert.Contains("SYSTEM_USER_INFO", sql);
        Assert.Contains("SYSTEM_AUTHROLE", sql);
        Assert.Contains("a.USR_ROLE LIKE 'IDM%'", sql);
        Assert.DoesNotContain("MASTER_EMPLOYEE", sql);
        Assert.DoesNotContain("EMP_STATUS", sql);
        Assert.DoesNotContain("USER_STATUS", sql);
        Assert.Contains("ORDER BY UserName, SourceRoleKey", sql);
    }

    [Theory]
    [InlineData("IDM01", "ADMINISTRATOR")]
    [InlineData("IDM02", "PRODUCTION")]
    [InlineData("IDM03", "STORE")]
    [InlineData("IDM04", "CHEMICAL")]
    [InlineData("IDM05", "CHEMICAL FOR ENGINEER")]
    public void Map_Should_PreserveFullCatalog(string key, string display)
    {
        string expectedUserName = $@"asia\test.{Guid.NewGuid():N}";
        var row = new IdmApplicationUserRow
        {
            EmployeeNo = "26008200",
            UserName = expectedUserName,
            SourceRoleKey = key,
            SourceRoleDisplayName = display,
            LastLoginAt = new DateTime(2026, 8, 20)
        };

        ApplicationUserRecord result = IdmApplicationUserProvider.Map(row);

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
