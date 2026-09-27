using System.Data;
using Dapper;
using RoleValidation.Application.Employees;
using RoleValidation.Core.Features.RoleValidation;
using RoleValidation.Infrastructure.Employees;

namespace RoleValidation.Infrastructure.Tests.Employees;

public sealed class OracleEmployeeReaderTests
{
    [Fact]
    public void Map_Should_MapEmployeeMasterFields()
    {
        string expectedUserName = $"test.{Guid.NewGuid():N}";
        var row = new OracleEmployeeRow
        {
            EmployeeNo = "00234053",
            UserName = expectedUserName,
            EmployeeName = "Jane Smith",
            EmployeeStatusCode = "A",
            JoinDate = new DateTime(2024, 11, 6),
            Email = "jane.smith@example.com",
            Position = "Operations Engineering Manager 1",
            Department = "Operations Engineering"
        };

        var record = OracleEmployeeReader.Map(row);

        Assert.Equal("00234053", record.EmployeeNo);
        Assert.Equal(expectedUserName, record.UserName);
        Assert.Equal("Jane Smith", record.EmployeeName);
        Assert.Equal(EmployeeStatusType.Active, record.Status.StatusType);
        Assert.Equal(new DateTime(2024, 11, 6), record.JoinDate);
        Assert.Equal("jane.smith@example.com", record.Email);
        Assert.Equal("Operations Engineering Manager 1", record.Position);
        Assert.Equal("Operations Engineering", record.Department);
    }

    [Fact]
    public void Map_Should_MapInactiveStatusCodeD()
    {
        var row = new OracleEmployeeRow
        {
            EmployeeNo = "00234050",
            EmployeeStatusCode = "D"
        };

        var record = OracleEmployeeReader.Map(row);

        Assert.Equal(EmployeeStatusType.Inactive, record.Status.StatusType);
    }

    [Fact]
    public void Map_Should_AllowNullablePositionAndDepartment()
    {
        var row = new OracleEmployeeRow
        {
            EmployeeNo = "00230100",
            EmployeeStatusCode = null,
            Position = null,
            Department = null
        };

        var record = OracleEmployeeReader.Map(row);

        Assert.Equal(EmployeeStatusType.Unknown, record.Status.StatusType);
        Assert.Null(record.Position);
        Assert.Null(record.Department);
    }

    [Fact]
    public void CreateSearchCommand_Should_UseExactNumberOrPartialNameWithFixedServerCap()
    {
        CommandDefinition command = OracleEmployeeReader.CreateSearchCommand(
            " Jane Smith ",
            requestedLimit: 500);

        Assert.Contains("RTRIM(e.EMP_NO) = :Query", command.CommandText);
        Assert.Contains("LIKE :NameQuery", command.CommandText);
        Assert.Contains("FETCH FIRST 25 ROWS ONLY", command.CommandText);
        Assert.DoesNotContain("EMP_EMAIL", command.CommandText);
        var parameters = Assert.IsType<DynamicParameters>(command.Parameters);
        Assert.Equal(
            ["NameQuery", "Query"],
            parameters.ParameterNames.Order().ToArray());
        Assert.Equal("Jane Smith", parameters.Get<string>("Query"));
        Assert.Equal("%JANE SMITH%", parameters.Get<string>("NameQuery"));
    }

    [Fact]
    public void CreateSearchCommand_Should_EscapeNameWildcardsAsLiteralText()
    {
        CommandDefinition command = OracleEmployeeReader.CreateSearchCommand(
            @"Ann_%\QA",
            requestedLimit: 25);
        var parameters = Assert.IsType<DynamicParameters>(command.Parameters);

        Assert.Equal(@"%ANN\_\%\\QA%", parameters.Get<string>("NameQuery"));
        Assert.Contains("ESCAPE '\\'", command.CommandText);
    }

    [Fact]
    public void MapSearch_Should_ReturnApprovedFieldsAndCurrentActivityOnly()
    {
        var row = new OracleEmployeeSearchRow
        {
            EmployeeNo = "00234053 ",
            EmployeeName = " Jane Smith ",
            EmployeeStatusCode = "D",
            Position = " Manager ",
            Department = " Operations "
        };

        EmployeeSearchResult result = OracleEmployeeReader.MapSearch(row);

        Assert.Equal("00234053", result.EmployeeNo);
        Assert.Equal("Jane Smith", result.DisplayName);
        Assert.Equal("Operations", result.Department);
        Assert.Equal("Manager", result.Position);
        Assert.Equal(EmployeeStatusType.Inactive, result.Status);
    }

    [Fact]
    public void CreateSearchCommand_Should_RejectBlankQuery()
    {
        Assert.Throws<ArgumentException>(() =>
            OracleEmployeeReader.CreateSearchCommand("  ", 25));
    }
}
