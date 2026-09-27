using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using RoleValidation.Application.Exports;
using RoleValidation.Application.RoleValidation;
using RoleValidation.Core.Features.RoleValidation;
using RoleValidation.Core.Features.SourceMappings;
using RoleValidation.Infrastructure.Exports;

namespace RoleValidation.Infrastructure.Tests.Exports;

public sealed class OpenXmlApplicationUserWorkbookExporterTests
{
    private static readonly string DefaultUserName = $"test.{Guid.NewGuid():N}";

    private static readonly DateTimeOffset ExportedAt =
        new(2026, 8, 24, 6, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void Export_Should_CreateConfirmedErsnWorkbook()
    {
        byte[] workbook = Export(CreateUser(
            lastLoginAt: new DateTime(2026, 8, 20, 9, 30, 0)));

        using var archive = new ZipArchive(
            new MemoryStream(workbook),
            ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
        Assert.NotNull(archive.GetEntry("xl/styles.xml"));

        string workbookXml = ReadEntry(archive, "xl/workbook.xml");
        string sheetXml = ReadEntry(archive, "xl/worksheets/sheet1.xml");

        Assert.Contains("Current Accounts", workbookXml);
        Assert.Contains("Application Name", sheetXml);
        Assert.Contains("Export Date", sheetXml);
        Assert.Contains("Exported By", sheetXml);
        Assert.Contains("2026-08-24 06:00 +07:00", sheetXml);
        Assert.Contains("Username", sheetXml);
        Assert.Contains("Roles (New Roles)", sheetXml);
        Assert.Contains("Days Since Last Login", sheetXml);
        Assert.Contains("Validate?", sheetXml);
        Assert.Contains(DefaultUserName, sheetXml);
        Assert.Contains("Administrator", sheetXml);
    }

    [Fact]
    public void Export_Should_PassOpenXmlValidation()
    {
        byte[] workbook = Export(CreateUser(
            lastLoginAt: new DateTime(2026, 8, 20, 9, 30, 0)));

        using SpreadsheetDocument document = Open(workbook);
        var validator = new OpenXmlValidator();

        var errors = validator.Validate(document).ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void Export_Should_PlaceDaysAndValidateColumnsInRequiredOrder()
    {
        byte[] workbook = Export(CreateUser(
            lastLoginAt: new DateTime(2026, 8, 20, 9, 30, 0)));

        using SpreadsheetDocument document = Open(workbook);
        Worksheet worksheet = GetWorksheet(document);

        Assert.Equal("Department", GetCellText(GetCell(worksheet, "G5")));
        Assert.Equal("Days Since Last Login", GetCellText(GetCell(worksheet, "H5")));
        Assert.Equal("Validate?", GetCellText(GetCell(worksheet, "I5")));
        Assert.Equal("4", GetCellText(GetCell(worksheet, "H6")));
        Assert.Equal(string.Empty, GetCellText(GetCell(worksheet, "I6")));
    }

    [Fact]
    public void Export_Should_CreateNativeYesNoValidationAndConditionalColors()
    {
        byte[] workbook = Export(
            CreateUser(new DateTime(2026, 8, 20, 9, 30, 0)),
            CreateUser(null, employeeNo: "00234054", userName: $"{DefaultUserName}.other"));

        using SpreadsheetDocument document = Open(workbook);
        Worksheet worksheet = GetWorksheet(document);

        DataValidations validations =
            Assert.IsType<DataValidations>(
                worksheet.GetFirstChild<DataValidations>());
        DataValidation validation =
            Assert.Single(validations.Elements<DataValidation>());

        Assert.Equal("I6:I7", validation.SequenceOfReferences?.InnerText);
        Assert.Equal(@"""Yes,No""", validation.Formula1?.Text);
        Assert.True(validation.ShowErrorMessage?.Value);
        Assert.True(validation.ShowInputMessage?.Value);

        ConditionalFormatting formatting =
            Assert.Single(worksheet.Elements<ConditionalFormatting>());
        Assert.Equal("I6:I7", formatting.SequenceOfReferences?.InnerText);

        ConditionalFormattingRule[] rules =
            formatting.Elements<ConditionalFormattingRule>().ToArray();
        Assert.Equal(2, rules.Length);
        Assert.Equal(@"$I6=""Yes""", Assert.Single(rules[0].Elements<Formula>()).Text);
        Assert.Equal(@"$I6=""No""", Assert.Single(rules[1].Elements<Formula>()).Text);

        Stylesheet styles = document.WorkbookPart!
            .WorkbookStylesPart!
            .Stylesheet
            ?? throw new InvalidOperationException(
                "Workbook stylesheet is missing.");
        Assert.NotNull(styles.CellStyleFormats);
        Assert.NotNull(styles.CellStyles);

        string[] fillColors = styles.Fills!
            .Elements<Fill>()
            .Select(fill =>
                fill.PatternFill?.ForegroundColor?.Rgb?.Value ?? string.Empty)
            .ToArray();
        Assert.Contains("FFFFF2CC", fillColors);

        string[] differentialColors = styles.DifferentialFormats!
            .Elements<DifferentialFormat>()
            .Select(format =>
                format.Fill?
                    .PatternFill?
                    .ForegroundColor?
                    .Rgb?
                    .Value ?? string.Empty)
            .ToArray();
        Assert.Equal(["FFE2F0D9", "FFF4CCCC"], differentialColors);

        string[] differentialBackgroundColors = styles.DifferentialFormats!
            .Elements<DifferentialFormat>()
            .Select(format =>
                format.Fill?
                    .PatternFill?
                    .BackgroundColor?
                    .Rgb?
                    .Value ?? string.Empty)
            .ToArray();
        Assert.Equal(
            ["FFE2F0D9", "FFF4CCCC"],
            differentialBackgroundColors);

        string[] differentialFontColors = styles.DifferentialFormats!
            .Elements<DifferentialFormat>()
            .Select(format =>
                format.Font?
                    .Color?
                    .Rgb?
                    .Value ?? string.Empty)
            .ToArray();
        Assert.Equal(["FF166534", "FF991B1B"], differentialFontColors);
    }

    [Fact]
    public void Export_Should_DisplayDash_WhenLastLoginIsMissing()
    {
        byte[] workbook = Export(CreateUser(lastLoginAt: null));

        using SpreadsheetDocument document = Open(workbook);
        Worksheet worksheet = GetWorksheet(document);

        Assert.Equal("-", GetCellText(GetCell(worksheet, "H6")));
    }

    [Fact]
    public void Export_Should_DisplayDash_WhenEmployeeNumberIsMissing()
    {
        byte[] workbook = Export(CreateUser(
            lastLoginAt: null,
            employeeNo: " "));

        using SpreadsheetDocument document = Open(workbook);
        Worksheet worksheet = GetWorksheet(document);

        Assert.Equal("-", GetCellText(GetCell(worksheet, "C6")));
    }

    [Fact]
    public void Export_Should_LeaveIcplbEmployeeNumberBlankWhenEmployeeIsUnmatched()
    {
        byte[] workbook = ExportIcplb(CreateUser(
            lastLoginAt: null,
            employeeNo: " "));

        using SpreadsheetDocument document = Open(workbook);
        Worksheet worksheet = GetWorksheet(document);

        Assert.Equal(string.Empty, GetCellText(GetCell(worksheet, "C6")));
    }

    [Fact]
    public void Export_Should_AddMfmCustomerCodeAndShiftTrailingControls()
    {
        byte[] workbook = ExportMfm(CreateUser(
            lastLoginAt: new DateTime(2026, 8, 20, 9, 30, 0),
            sourceRoleKey: "CUSCI",
            sourceRoleDisplayName: "CI",
            roleName: "Buyer-User"));

        using SpreadsheetDocument document = Open(workbook);
        Worksheet worksheet = GetWorksheet(document);

        Assert.Equal("Roles (New Roles)", GetCellText(GetCell(worksheet, "F5")));
        Assert.Equal("CustomerCode", GetCellText(GetCell(worksheet, "G5")));
        Assert.Equal("Department", GetCellText(GetCell(worksheet, "H5")));
        Assert.Equal("Days Since Last Login", GetCellText(GetCell(worksheet, "I5")));
        Assert.Equal("Validate?", GetCellText(GetCell(worksheet, "J5")));
        Assert.Equal("Buyer-User", GetCellText(GetCell(worksheet, "F6")));
        Assert.Equal("CI", GetCellText(GetCell(worksheet, "G6")));
        Assert.Equal("4", GetCellText(GetCell(worksheet, "I6")));

        DataValidation validation = Assert.Single(
            worksheet
                .GetFirstChild<DataValidations>()!
                .Elements<DataValidation>());
        Assert.Equal("J6:J6", validation.SequenceOfReferences?.InnerText);

        ConditionalFormatting formatting = Assert.Single(
            worksheet.Elements<ConditionalFormatting>());
        Assert.Equal("J6:J6", formatting.SequenceOfReferences?.InnerText);
        Assert.Equal(
            @"$J6=""Yes""",
            Assert.Single(
                formatting
                    .Elements<ConditionalFormattingRule>()
                    .First()
                    .Elements<Formula>())
                .Text);
    }

    private static byte[] Export(params ApplicationUserView[] users)
    {
        var exporter = new OpenXmlApplicationUserWorkbookExporter();

        return exporter.Export(
            applicationName: "eRSN",
            profile: ApplicationUserExportProfile.ForApplication("ERSN"),
            exportedAt: ExportedAt,
            exportedBy: "00230001",
            users: users);
    }

    private static byte[] ExportMfm(params ApplicationUserView[] users)
    {
        var exporter = new OpenXmlApplicationUserWorkbookExporter();

        return exporter.Export(
            applicationName: "MFM Plus",
            profile: ApplicationUserExportProfile.ForApplication("MFM"),
            exportedAt: ExportedAt,
            exportedBy: "00230001",
            users: users);
    }

    private static byte[] ExportIcplb(params ApplicationUserView[] users)
    {
        var exporter = new OpenXmlApplicationUserWorkbookExporter();

        return exporter.Export(
            applicationName: "IC Programming",
            profile: ApplicationUserExportProfile.ForApplication("ICPLB"),
            exportedAt: ExportedAt,
            exportedBy: "00230001",
            users: users);
    }

    private static ApplicationUserView CreateUser(
        DateTime? lastLoginAt,
        string employeeNo = "00234053",
        string? userName = null,
        string sourceRoleKey = "RSN_ADMIN",
        string sourceRoleDisplayName = "eRSN Administrator",
        string roleName = "Administrator")
    {
        userName ??= DefaultUserName;

        return new ApplicationUserView(
            employeeNo,
            employeeName: "Jane Smith",
            employeeStatus: new EmployeeStatus("A"),
            sourceRoleKey,
            sourceRoleDisplayName,
            roleId: 10,
            roleName,
            resolutionType: SourceRoleResolutionType.Resolved,
            userName,
            email: $"{userName}@example.test",
            position: "Operations Engineering Manager 1",
            department: "Operations Engineering",
            lastLoginAt);
    }

    private static SpreadsheetDocument Open(byte[] workbook)
    {
        return SpreadsheetDocument.Open(
            new MemoryStream(workbook),
            isEditable: false);
    }

    private static Worksheet GetWorksheet(SpreadsheetDocument document)
    {
        return document.WorkbookPart
            ?.WorksheetParts
            .Single()
            .Worksheet
            ?? throw new InvalidOperationException(
                "Workbook worksheet is missing.");
    }

    private static Cell GetCell(Worksheet worksheet, string reference)
    {
        return worksheet
            .Descendants<Cell>()
            .Single(cell => cell.CellReference?.Value == reference);
    }

    private static string GetCellText(Cell cell)
    {
        return cell.InlineString?.InnerText
            ?? cell.CellValue?.Text
            ?? string.Empty;
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using Stream stream = archive.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
