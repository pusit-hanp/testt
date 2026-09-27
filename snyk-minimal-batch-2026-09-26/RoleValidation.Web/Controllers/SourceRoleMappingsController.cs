using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RoleValidation.Application.Administration;
using RoleValidation.Application.SourceMappings;
using RoleValidation.Application.Users;
using RoleValidation.Core.Features.Roles;
using RoleValidation.Core.Features.SourceMappings;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Models.SourceRoleMappings;

namespace RoleValidation.Web.Controllers;

public sealed class SourceRoleMappingsController : Controller
{
    private readonly ApplicationAdministrationHandler _applicationHandler;
    private readonly ValidationRoleAdministrationHandler _roleHandler;
    private readonly SourceMappingAdministrationHandler _mappingHandler;
    private readonly IApplicationUserReader _applicationUserReader;

    public SourceRoleMappingsController(
        ApplicationAdministrationHandler applicationHandler,
        ValidationRoleAdministrationHandler roleHandler,
        SourceMappingAdministrationHandler mappingHandler,
        IApplicationUserReader applicationUserReader)
    {
        _applicationHandler = applicationHandler
            ?? throw new ArgumentNullException(nameof(applicationHandler));
        _roleHandler = roleHandler
            ?? throw new ArgumentNullException(nameof(roleHandler));
        _mappingHandler = mappingHandler
            ?? throw new ArgumentNullException(nameof(mappingHandler));
        _applicationUserReader = applicationUserReader
            ?? throw new ArgumentNullException(nameof(applicationUserReader));
    }

    [HttpGet]
    [Authorize(Policy = RoleValidationAuthorizationPolicies.AdminRead)]
    public async Task<IActionResult> Index(
        int? applicationId,
        string? search,
        string? status,
        int? targetRoleId,
        CancellationToken cancellationToken = default)
    {
        SourceRoleMappingManagementViewModel model = await BuildModelAsync(
            applicationId,
            search,
            status,
            targetRoleId,
            TempData["ManagementError"] as string,
            cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Add(
        int applicationId,
        string? sourceKey,
        int roleId,
        CancellationToken cancellationToken = default)
    {
        string normalizedKey = sourceKey?.Trim() ?? string.Empty;
        PreserveEditor(
            mappingId: null,
            normalizedKey,
            roleId);
        if (applicationId <= 0)
        {
            return RedirectStableError(
                applicationId,
                "APPLICATION_NOT_FOUND");
        }
        if (roleId <= 0)
        {
            return RedirectStableError(applicationId, "ROLE_NOT_FOUND");
        }

        string? actor = GetActorEmployeeNo();
        if (actor is null)
        {
            return RedirectEditorError(
                applicationId,
                "Current employee identity is unavailable.",
                "mapping-editor-new",
                "mapping-error-new");
        }

        AdministrationResult result = await _mappingHandler.AddAsync(
            new AddSourceMappingCommand(
                applicationId,
                normalizedKey,
                roleId,
                actor),
            cancellationToken);
        if (result.Succeeded)
        {
            ClearEditor();
            string target = result.EntityId is int id
                ? $"mapping-action-{id}"
                : "page-heading";
            TempData["ManagementSuccess"] = "Source mapping added.";
            TempData["FocusTarget"] = target;
            return RedirectToIndex(applicationId, target);
        }

        if (IsPageFailure(result.ErrorCode))
        {
            ClearEditor();
            return RedirectStableError(
                applicationId,
                result.ErrorCode ?? "ADMINISTRATION_ERROR");
        }

        return RedirectEditorError(
            applicationId,
            ErrorMessage(result.ErrorCode),
            "mapping-editor-new",
            "mapping-error-new");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Replace(
        int applicationId,
        int sourceRoleMappingId,
        int roleId,
        CancellationToken cancellationToken = default)
    {
        PreserveEditor(
            sourceRoleMappingId,
            sourceKey: null,
            roleId);
        if (applicationId <= 0)
        {
            return RedirectStableError(
                applicationId,
                "APPLICATION_NOT_FOUND");
        }
        if (sourceRoleMappingId <= 0)
        {
            return RedirectStableError(
                applicationId,
                "SOURCE_MAPPING_NOT_FOUND");
        }
        if (roleId <= 0)
        {
            return RedirectStableError(applicationId, "ROLE_NOT_FOUND");
        }

        string editorTarget = $"mapping-editor-{sourceRoleMappingId}";
        string errorTarget = $"mapping-error-{sourceRoleMappingId}";
        string? actor = GetActorEmployeeNo();
        if (actor is null)
        {
            return RedirectEditorError(
                applicationId,
                "Current employee identity is unavailable.",
                editorTarget,
                errorTarget);
        }

        AdministrationResult result = await _mappingHandler.ReplaceAsync(
            new ReplaceSourceMappingCommand(
                applicationId,
                sourceRoleMappingId,
                roleId,
                actor),
            cancellationToken);
        if (result.Succeeded)
        {
            ClearEditor();
            string target = result.EntityId is int id
                ? $"mapping-action-{id}"
                : "page-heading";
            TempData["ManagementSuccess"] = result.EntityId ==
                sourceRoleMappingId
                ? "Source mapping unchanged."
                : "Source mapping replaced.";
            TempData["FocusTarget"] = target;
            return RedirectToIndex(applicationId, target);
        }

        if (IsPageFailure(result.ErrorCode))
        {
            ClearEditor();
            return RedirectStableError(
                applicationId,
                result.ErrorCode ?? "ADMINISTRATION_ERROR");
        }

        return RedirectEditorError(
            applicationId,
            ErrorMessage(result.ErrorCode),
            editorTarget,
            errorTarget);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Deactivate(
        int applicationId,
        int sourceRoleMappingId,
        CancellationToken cancellationToken = default)
    {
        if (applicationId <= 0)
        {
            return RedirectStableError(
                applicationId,
                "APPLICATION_NOT_FOUND");
        }
        if (sourceRoleMappingId <= 0)
        {
            return RedirectStableError(
                applicationId,
                "SOURCE_MAPPING_NOT_FOUND");
        }

        string? actor = GetActorEmployeeNo();
        if (actor is null)
        {
            return RedirectStableError(
                applicationId,
                "Current employee identity is unavailable.");
        }

        AdministrationResult result = await _mappingHandler.DeactivateAsync(
            new DeactivateSourceMappingCommand(
                applicationId,
                sourceRoleMappingId,
                actor),
            cancellationToken);
        if (result.Succeeded)
        {
            TempData["ManagementSuccess"] = "Source mapping deactivated.";
            TempData["FocusTarget"] = "page-heading";
            return RedirectToIndex(applicationId, "page-heading");
        }

        return RedirectStableError(
            applicationId,
            result.ErrorCode ?? "ADMINISTRATION_ERROR");
    }

    private async Task<SourceRoleMappingManagementViewModel> BuildModelAsync(
        int? applicationId,
        string? search,
        string? status,
        int? targetRoleId,
        string? error,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicationAdministrationRow> applications =
            await _applicationHandler.GetApplicationsAsync(cancellationToken);
        int? selectedId = applicationId is null
            ? applications.FirstOrDefault()?.ApplicationId
            : applicationId;
        ApplicationAdministrationRow? selected = applications.FirstOrDefault(
            application => application.ApplicationId == selectedId);
        IReadOnlyList<ValidationRole> roles = [];
        IReadOnlyList<SourceRoleMappingManagementRow> mappings = [];
        if (selectedId.HasValue && selected is null)
        {
            selectedId = null;
            error ??=
                "APPLICATION_NOT_FOUND: The selected Application was not found.";
        }
        else if (selected is not null)
        {
            roles = await _roleHandler.GetByApplicationAsync(
                selected.ApplicationId,
                cancellationToken);
            IReadOnlyDictionary<int, string> roleNames = roles.ToDictionary(
                role => role.RoleId,
                role => role.RoleName);
            IReadOnlyList<SourceRoleMapping> sourceMappings =
                await _mappingHandler.GetByApplicationAsync(
                    selected.ApplicationId,
                    cancellationToken);
            IReadOnlyDictionary<string, string> legacyDisplayNames =
                BuildLegacyDisplayNames(await _applicationUserReader.GetUsersAsync(
                    selected.ApplicationId,
                    cancellationToken));
            mappings = sourceMappings
                .Select(mapping => new SourceRoleMappingManagementRow(
                    mapping.RoleMapId,
                    mapping.SourceKey,
                    legacyDisplayNames.TryGetValue(
                        mapping.SourceKey,
                        out string? legacyDisplayName)
                        ? legacyDisplayName
                        : mapping.SourceDisplayName,
                    mapping.RoleId,
                    roleNames.TryGetValue(mapping.RoleId, out string? roleName)
                        ? roleName
                        : $"ROLE_ID {mapping.RoleId}",
                    mapping.IsActive))
                .Where(mapping => Matches(
                    mapping,
                    search,
                    status,
                    targetRoleId))
                .OrderByDescending(mapping => mapping.IsActive)
                .ThenBy(mapping => mapping.SourceKey, StringComparer.Ordinal)
                .ThenBy(mapping => mapping.SourceRoleMappingId)
                .ToList();
        }

        return new SourceRoleMappingManagementViewModel
        {
            Applications = applications,
            SelectedApplicationId = selectedId,
            SelectedApplicationName = selected?.ApplicationName,
            ActiveRoles = roles
                .Where(role => role.IsActive)
                .OrderBy(role => role.RoleName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FilterRoles = roles
                .OrderByDescending(role => role.IsActive)
                .ThenBy(role => role.RoleName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Mappings = mappings,
            CanManage = User.IsInRole("Local_IT_Admin"),
            Search = NormalizeOptional(search),
            Status = NormalizeStatus(status),
            TargetRoleId = targetRoleId is > 0 ? targetRoleId : null,
            SuccessMessage = TempData["ManagementSuccess"] as string,
            ErrorMessage = error,
            DrawerErrorTarget = TempData["DrawerErrorTarget"] as string,
            FocusTarget = TempData["FocusTarget"] as string,
            EditorMappingId = ReadTempInt("EditorMappingId"),
            EditorSourceKey = TempData["EditorSourceKey"] as string,
            EditorRoleId = ReadTempInt("EditorRoleId")
        };
    }

    private int? ReadTempInt(string tempDataName) =>
        TempData[tempDataName] is int value ? value : null;

    private static bool Matches(
        SourceRoleMappingManagementRow mapping,
        string? search,
        string? status,
        int? targetRoleId)
    {
        string? normalizedSearch = NormalizeOptional(search);
        if (normalizedSearch is not null &&
            !mapping.SourceKey.Contains(
                normalizedSearch,
                StringComparison.OrdinalIgnoreCase) &&
            !(mapping.SourceDisplayName?.Contains(
                normalizedSearch,
                StringComparison.OrdinalIgnoreCase) ?? false) &&
            !mapping.RoleName.Contains(
                normalizedSearch,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? normalizedStatus = NormalizeStatus(status);
        if (normalizedStatus == "active" && !mapping.IsActive ||
            normalizedStatus == "inactive" && mapping.IsActive)
        {
            return false;
        }

        return targetRoleId is not > 0 || mapping.RoleId == targetRoleId;
    }

    private string? GetActorEmployeeNo()
    {
        string? value = User.FindFirstValue(
            RoleValidationAuthenticationDefaults.EmployeeNoClaimType);
        return NormalizeOptional(value);
    }

    private void PreserveEditor(
        int? mappingId,
        string? sourceKey,
        int roleId)
    {
        TempData["EditorMappingId"] = mappingId;
        TempData["EditorSourceKey"] = sourceKey;
        TempData["EditorRoleId"] = roleId;
    }

    private void ClearEditor()
    {
        TempData.Remove("EditorMappingId");
        TempData.Remove("EditorSourceKey");
        TempData.Remove("EditorRoleId");
        TempData.Remove("DrawerErrorTarget");
    }

    private RedirectToActionResult RedirectEditorError(
        int applicationId,
        string error,
        string drawerTarget,
        string focusTarget)
    {
        TempData["ManagementError"] = error;
        TempData["DrawerErrorTarget"] = drawerTarget;
        TempData["FocusTarget"] = focusTarget;
        return RedirectToIndex(applicationId, drawerTarget);
    }

    private RedirectToActionResult RedirectStableError(
        int applicationId,
        string errorCode)
    {
        TempData["ManagementError"] = ErrorMessage(errorCode);
        TempData["FocusTarget"] = "page-heading";
        return RedirectToIndex(applicationId, "page-heading");
    }

    private RedirectToActionResult RedirectToIndex(
        int applicationId,
        string fragment)
    {
        return RedirectToAction(
            nameof(Index),
            controllerName: null,
            routeValues: new
            {
                applicationId = applicationId > 0
                    ? applicationId
                    : (int?)null
            },
            fragment: fragment);
    }

    private static bool IsPageFailure(string? errorCode) => errorCode is
        "APPLICATION_NOT_FOUND" or
        "ROLE_NOT_FOUND" or
        "ROLE_INACTIVE" or
        "SOURCE_MAPPING_NOT_FOUND" or
        "SOURCE_MAPPING_INACTIVE";

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeStatus(string? value)
    {
        string? normalized = NormalizeOptional(value)?.ToLowerInvariant();
        return normalized is "active" or "inactive" ? normalized : null;
    }

    private static string ErrorMessage(string? errorCode) => errorCode switch
    {
        "APPLICATION_NOT_FOUND" =>
            "APPLICATION_NOT_FOUND: The selected Application was not found.",
        "SOURCE_KEY_REQUIRED" =>
            "SOURCE_KEY_REQUIRED: Enter a source key.",
        "SOURCE_KEY_TOO_LONG" =>
            "SOURCE_KEY_TOO_LONG: Source key cannot exceed 200 characters.",
        "SOURCE_MAPPING_DUPLICATE" =>
            "SOURCE_MAPPING_DUPLICATE: This active source key already exists.",
        "SOURCE_MAPPING_NOT_FOUND" =>
            "SOURCE_MAPPING_NOT_FOUND: The source mapping was not found in the selected Application.",
        "SOURCE_MAPPING_INACTIVE" =>
            "SOURCE_MAPPING_INACTIVE: The source mapping is inactive and read only.",
        "ROLE_NOT_FOUND" =>
            "ROLE_NOT_FOUND: The Validation Role was not found in the selected Application.",
        "ROLE_INACTIVE" =>
            "ROLE_INACTIVE: Choose an active Validation Role.",
        _ when errorCode?.Contains(' ') == true => errorCode,
        _ => $"{errorCode ?? "ADMINISTRATION_ERROR"}: " +
             "The source mapping change was not saved."
    };

    private static IReadOnlyDictionary<string, string> BuildLegacyDisplayNames(
        IReadOnlyList<ApplicationUserRecord> users)
    {
        return users
            .Where(user => !string.IsNullOrWhiteSpace(
                user.SourceRoleDisplayName))
            .GroupBy(user => user.SourceRoleKey, StringComparer.Ordinal)
            .Select(group => new
            {
                SourceKey = group.Key,
                DisplayNames = group
                    .Select(user => user.SourceRoleDisplayName!)
                    .Distinct(StringComparer.Ordinal)
                    .Take(2)
                    .ToList()
            })
            .Where(item => item.DisplayNames.Count == 1)
            .ToDictionary(
                item => item.SourceKey,
                item => item.DisplayNames[0],
                StringComparer.Ordinal);
    }
}
