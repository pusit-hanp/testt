using System.Data.Common;
using System.Globalization;
using RoleValidation.Application.Users;
using RoleValidation.Infrastructure.ApplicationUsers;
using RoleValidation.Infrastructure.Database;

namespace RoleValidation.Infrastructure.Tests.ApplicationUsers;

public sealed class IcProgrammingApplicationUserProviderTests
{
    [Fact]
    public void Provider_Should_UseAppSimConnectionAndStableApplicationCode()
    {
        var provider = new IcProgrammingApplicationUserProvider(
            new StubAppSimOracleConnectionFactory());

        Assert.Equal("ICPLB", provider.ApplicationCode);
    }

    [Fact]
    public void CreateCommand_Should_ReadMenuNameWithoutReplacingGroupKey()
    {
        string sql = IcProgrammingApplicationUserProvider
            .CreateCommand()
            .CommandText;

        Assert.Contains("APPSIM.AS_GROUP_USAGE", sql);
        Assert.Contains("TRIM(u.GROUP_NAME) AS SourceRoleKey", sql);
        Assert.Contains("TRIM(m.NAME) AS SourceRoleDisplayName", sql);
        Assert.Contains("APPSIM.AS_GROUP_MENU", sql);
        Assert.Contains("APPSIM.AS_MENU", sql);
        Assert.Contains("gm.project_name = 'ICPLB'", sql);
        Assert.Contains("m.project_name = 'ICPLB'", sql);
        Assert.Contains("u.PROJECT_NAME = 'ICPLB'", sql);
        Assert.DoesNotContain("m.NAME IS NOT NULL", sql);
        Assert.DoesNotContain("MASTER_EMPLOYEE", sql);
    }

    [Theory]
    [InlineData("ADMINISTRATOR", "User Maintenance")]
    [InlineData("ADMINISTRATOR", "Role Maintenance")]
    public void Map_Should_PreserveGroupKeyAndUseMenuNameAsDisplayName(
        string groupName,
        string menuName)
    {
        string employeeNo = Random.Shared.Next(10_000_000, 100_000_000)
            .ToString(CultureInfo.InvariantCulture);
        string expectedUserName = $"th{employeeNo}";
        var row = new IcProgrammingApplicationUserRow
        {
            UserName = expectedUserName,
            SourceRoleKey = groupName,
            SourceRoleDisplayName = menuName
        };

        ApplicationUserRecord result =
            IcProgrammingApplicationUserProvider.Map(row);

        Assert.Equal(string.Empty, result.EmployeeNo);
        Assert.Equal(groupName, result.SourceRoleKey);
        Assert.Equal(menuName, result.SourceRoleDisplayName);
        Assert.Equal(expectedUserName, result.UserName);
        Assert.Null(result.LastLoginAt);
    }

    [Fact]
    public void Map_Should_FallBackToGroupWhenMenuNameIsMissing()
    {
        string employeeNo = Random.Shared.Next(10_000_000, 100_000_000)
            .ToString(CultureInfo.InvariantCulture);
        var row = new IcProgrammingApplicationUserRow
        {
            UserName = $"th{employeeNo}",
            SourceRoleKey = "EMPLOYEE"
        };

        ApplicationUserRecord result =
            IcProgrammingApplicationUserProvider.Map(row);

        Assert.Equal("EMPLOYEE", result.SourceRoleDisplayName);
    }

    private sealed class StubAppSimOracleConnectionFactory
        : IAppSimOracleConnectionFactory
    {
        public DbConnection CreateConnection() =>
            throw new NotSupportedException();
    }
}
