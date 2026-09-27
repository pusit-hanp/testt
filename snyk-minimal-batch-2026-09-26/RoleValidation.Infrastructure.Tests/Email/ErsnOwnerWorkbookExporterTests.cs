using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using RoleValidation.Application.Email;
using RoleValidation.Application.Exports;
using RoleValidation.Application.RoleValidation;
using RoleValidation.Core.Features.RoleValidation;
using RoleValidation.Core.Features.SourceMappings;
using RoleValidation.Infrastructure.Email;
using RoleValidation.Infrastructure.Exports;

namespace RoleValidation.Infrastructure.Tests.Email;

public sealed class ErsnOwnerWorkbookExporterTests
{
    private static readonly string TestRunId = Guid.NewGuid().ToString("N");

    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 8, 31, 9, 15, 0, TimeSpan.FromHours(7));

    [Theory]
    [InlineData("ERSN", null)]
    [InlineData("PULL_LIST", null)]
    [InlineData("IDM", null)]
    [InlineData("MFM", "CustomerCode")]
    [InlineData("EVA", null)]
    [InlineData("ICPLB", null)]
    [InlineData("OPM", null)]
    public void Export_Should_DelegateApprovedApplicationProfileWithDefensiveScopeAndDeduplication(
        string applicationCode,
        string? expectedSourceRoleHeader)
    {
        var inner = new RecordingWorkbookExporter([1, 2, 3]);
        var exporter = new OpenXmlErsnOwnerWorkbookExporter(inner);
        ApplicationUserView approved = User(
            "approved",
            roleId: 10,
            audited: true,
            SourceRoleResolutionType.Resolved);
        OwnerScope scope = Scope(
            applicationCode,
            [10],
            [
                approved,
                approved,
                User("wrong-role", 11, true, SourceRoleResolutionType.Resolved),
                User("wrong-audit", 10, false, SourceRoleResolutionType.Resolved),
                User("wrong-resolution", 10, true, SourceRoleResolutionType.NotMapped)
            ]);

        byte[] result = exporter.Export(
            scope,
            GeneratedAt,
            " C1008267 ");

        Assert.Equal([1, 2, 3], result);
        Assert.Equal(1, inner.CallCount);
        Assert.Equal("eRSN", inner.ApplicationName);
        Assert.Equal(
            expectedSourceRoleHeader,
            inner.Profile!.SourceRoleDisplayColumnHeader);
        Assert.Equal(GeneratedAt, inner.ExportedAt);
        Assert.Equal("C1008267", inner.ExportedBy);
        Assert.Equal([approved], inner.Users);
    }

    [Fact]
    public void Export_Should_UseMfmCustomerCodeColumnInCurrentAccountsWorkbook()
    {
        var exporter = new OpenXmlErsnOwnerWorkbookExporter(
            new OpenXmlApplicationUserWorkbookExporter());
        OwnerScope scope = Scope(
            " MFM ",
            [10],
            [User(
                "CUSCI",
                roleId: 10,
                audited: true,
                SourceRoleResolutionType.Resolved)]);

        byte[] bytes = exporter.Export(scope, GeneratedAt, "C1008267");

        using SpreadsheetDocument document = SpreadsheetDocument.Open(
            new MemoryStream(bytes),
            isEditable: false);
        Assert.Empty(new OpenXmlValidator().Validate(document));

        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidOperationException(
                "Workbook part is missing.");
        Worksheet worksheet = workbookPart
            .WorksheetParts
            .Single()
            .Worksheet
            ?? throw new InvalidOperationException(
                "Worksheet is missing.");
        string[] headers = worksheet
            .Descendants<Row>()
            .Single(row => row.RowIndex?.Value == 5U)
            .Elements<Cell>()
            .Select(cell => cell.InlineString?.InnerText ?? string.Empty)
            .ToArray();
        Assert.Equal(
            [
                "Username",
                "Full Name",
                "Emp Number",
                "PositionWP",
                "Email address",
                "Roles (New Roles)",
                "CustomerCode",
                "Department",
                "Days Since Last Login",
                "Validate?"
            ],
            headers);
    }

    [Fact]
    public void Export_Should_PreservePhaseOneSheetHeadersAndOpenXmlValidity()
    {
        var exporter = new OpenXmlErsnOwnerWorkbookExporter(
            new OpenXmlApplicationUserWorkbookExporter());
        OwnerScope scope = Scope(
            " eRsN ",
            [10],
            [User(
                "approved",
                roleId: 10,
                audited: true,
                SourceRoleResolutionType.Resolved)]);

        byte[] bytes = exporter.Export(scope, GeneratedAt, "C1008267");

        using SpreadsheetDocument document = SpreadsheetDocument.Open(
            new MemoryStream(bytes),
            isEditable: false);
        var errors = new OpenXmlValidator().Validate(document).ToList();
        Assert.Empty(errors);

        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidOperationException(
                "Workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidOperationException(
                "Workbook is missing.");
        Sheets sheets = workbook.Sheets
            ?? throw new InvalidOperationException(
                "Workbook sheets are missing.");
        Sheet sheet = Assert.Single(sheets
            .Elements<Sheet>());
        Assert.Equal("Current Accounts", sheet.Name?.Value);
        Worksheet worksheet = workbookPart
            .WorksheetParts
            .Single()
            .Worksheet
            ?? throw new InvalidOperationException(
                "Worksheet is missing.");
        string[] headers = worksheet
            .Descendants<Row>()
            .Single(row => row.RowIndex?.Value == 5U)
            .Elements<Cell>()
            .Select(cell => cell.InlineString?.InnerText ?? string.Empty)
            .ToArray();
        Assert.Equal(
            [
                "Username",
                "Full Name",
                "Emp Number",
                "PositionWP",
                "Email address",
                "Roles (New Roles)",
                "Department",
                "Days Since Last Login",
                "Validate?"
            ],
            headers);
        Assert.DoesNotContain(headers, header => header.Contains(
            "Owner",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Export_Should_CreateApprovedValidWorkbookWhenPendingOwnerHasNoRows()
    {
        var exporter = new OpenXmlErsnOwnerWorkbookExporter(
            new OpenXmlApplicationUserWorkbookExporter());

        byte[] bytes = exporter.Export(
            Scope("ERSN", [10], []),
            GeneratedAt,
            "C1008267");

        using SpreadsheetDocument document = SpreadsheetDocument.Open(
            new MemoryStream(bytes),
            isEditable: false);
        Assert.Empty(new OpenXmlValidator().Validate(document));

        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidOperationException(
                "Workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidOperationException(
                "Workbook is missing.");
        Sheets sheets = workbook.Sheets
            ?? throw new InvalidOperationException(
                "Workbook sheets are missing.");
        Sheet sheet = Assert.Single(sheets.Elements<Sheet>());
        Assert.Equal("Current Accounts", sheet.Name?.Value);

        Worksheet worksheet = workbookPart
            .WorksheetParts
            .Single()
            .Worksheet
            ?? throw new InvalidOperationException(
                "Worksheet is missing.");
        string[] headers = worksheet
            .Descendants<Row>()
            .Single(row => row.RowIndex?.Value == 5U)
            .Elements<Cell>()
            .Select(cell => cell.InlineString?.InnerText ?? string.Empty)
            .ToArray();
        Assert.Equal(
            [
                "Username",
                "Full Name",
                "Emp Number",
                "PositionWP",
                "Email address",
                "Roles (New Roles)",
                "Department",
                "Days Since Last Login",
                "Validate?"
            ],
            headers);
        Assert.DoesNotContain(
            worksheet.Descendants<Row>(),
            row => row.RowIndex?.Value >= 6U);
    }

    [Fact]
    public void Export_Should_RejectUnknownScopeWithoutCallingInnerExporter()
    {
        var inner = new RecordingWorkbookExporter([1]);
        var exporter = new OpenXmlErsnOwnerWorkbookExporter(inner);

        InvalidOperationException error = Assert.Throws<
            InvalidOperationException>(() => exporter.Export(
                Scope("FUTURE_APP", [10], []),
                GeneratedAt,
                "C1008267"));

        Assert.Equal("EMAIL_WORKBOOK_CONTRACT_NOT_APPROVED", error.Message);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public void Export_Should_RejectFailedOwnerScopeWithoutCallingInnerExporter()
    {
        var inner = new RecordingWorkbookExporter([1]);
        var exporter = new OpenXmlErsnOwnerWorkbookExporter(inner);
        OwnerDeliverySeed failed = OwnerDeliverySeed.Failed(
            "C1000001",
            "OWNER_INACTIVE");

        Assert.Throws<InvalidOperationException>(() => exporter.Export(
            Scope("ERSN", [10], [], failed),
            GeneratedAt,
            "C1008267"));

        Assert.Equal(0, inner.CallCount);
    }

    private static OwnerScope Scope(
        string applicationCode,
        IReadOnlyList<int> roleIds,
        IReadOnlyList<ApplicationUserView> rows,
        OwnerDeliverySeed? deliverySeed = null) =>
        new(
            applicationId: 17,
            applicationCode,
            applicationName: "eRSN",
            ownerEmployeeNo: "C1000001",
            deliverySeed ?? OwnerDeliverySeed.Pending("C1000001"),
            roleIds,
            rows);

    private static ApplicationUserView User(
        string label,
        int roleId,
        bool audited,
        SourceRoleResolutionType resolutionType) =>
        new(
            employeeNo: "C2000001",
            employeeName: "Example User",
            employeeStatus: new EmployeeStatus("A"),
            sourceRoleKey: $"SOURCE_{label}",
            sourceRoleDisplayName: label,
            roleId,
            roleName: $"Role {roleId}",
            resolutionType,
            userName: $"{label}.{TestRunId}",
            email: "example.user@example.test",
            position: "Analyst",
            department: "Operations",
            lastLoginAt: new DateTime(2026, 8, 20),
            isAudited: audited);

    private sealed class RecordingWorkbookExporter(byte[] result)
        : IApplicationUserWorkbookExporter
    {
        public int CallCount { get; private set; }

        public string? ApplicationName { get; private set; }

        public ApplicationUserExportProfile? Profile { get; private set; }

        public DateTimeOffset ExportedAt { get; private set; }

        public string? ExportedBy { get; private set; }

        public IReadOnlyList<ApplicationUserView> Users { get; private set; } = [];

        public byte[] Export(
            string applicationName,
            ApplicationUserExportProfile profile,
            DateTimeOffset exportedAt,
            string exportedBy,
            IReadOnlyList<ApplicationUserView> users)
        {
            CallCount++;
            ApplicationName = applicationName;
            Profile = profile;
            ExportedAt = exportedAt;
            ExportedBy = exportedBy;
            Users = users;
            return result;
        }
    }
}
