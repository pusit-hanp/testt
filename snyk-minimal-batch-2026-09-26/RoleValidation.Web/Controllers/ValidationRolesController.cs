using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RoleValidation.Application.Administration;
using RoleValidation.Core.Features.Roles;
using RoleValidation.Web.Authentication;
using RoleValidation.Web.Models.ValidationRoles;

namespace RoleValidation.Web.Controllers;

public sealed class ValidationRolesController : Controller
{
    private readonly ApplicationAdministrationHandler _applicationHandler;
    private readonly ValidationRoleAdministrationHandler _roleHandler;

    public ValidationRolesController(
        ApplicationAdministrationHandler applicationHandler,
        ValidationRoleAdministrationHandler roleHandler)
    {
        _applicationHandler = applicationHandler
            ?? throw new ArgumentNullException(nameof(applicationHandler));
        _roleHandler = roleHandler
            ?? throw new ArgumentNullException(nameof(roleHandler));
    }

    [HttpGet]
    [Authorize(Policy = RoleValidationAuthorizationPolicies.AdminRead)]
    public async Task<IActionResult> Index(
        int? applicationId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApplicationAdministrationRow> applications =
            await _applicationHandler.GetApplicationsAsync(cancellationToken);
        int? selectedId = applicationId is null
            ? applications.FirstOrDefault()?.ApplicationId
            : applicationId;
        ApplicationAdministrationRow? selected = applications.FirstOrDefault(
            application => application.ApplicationId == selectedId);
        IReadOnlyList<ValidationRole> roles = [];
        string? error = TempData["ManagementError"] as string;

        if (selectedId.HasValue && selected is null)
        {
            selectedId = null;
            error ??=
                "APPLICATION_NOT_FOUND: The selected Application was not found.";
        }
        else if (selected is not null)
        {
            roles = (await _roleHandler.GetByApplicationAsync(
                    selected.ApplicationId,
                    cancellationToken))
                .OrderByDescending(role => role.IsActive)
                .ThenBy(role => role.RoleName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(role => role.RoleId)
                .ToList();
        }

        var model = new ValidationRoleManagementViewModel
        {
            Applications = applications,
            SelectedApplicationId = selectedId,
            SelectedApplicationName = selected?.ApplicationName,
            Roles = roles,
            CanManage = User.IsInRole("Local_IT_Admin"),
            SuccessMessage = TempData["ManagementSuccess"] as string,
            ErrorMessage = error,
            FocusTarget = TempData["FocusTarget"] as string,
            ReactivationRoleId = ReadTempDataInt("ReactivationRoleId"),
            EditorRoleId = ReadTempDataInt("EditorRoleId"),
            EditorRoleName = TempData["EditorRoleName"] as string,
            EditorIsAudited = ReadTempDataBool("EditorIsAudited")
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Save(
        int applicationId,
        int? roleId,
        string? roleName,
        bool isAudited,
        bool reactivate,
        CancellationToken cancellationToken = default)
    {
        string normalizedName = roleName?.Trim() ?? string.Empty;
        string? actorEmployeeNo = GetActorEmployeeNo();
        if (applicationId <= 0 || roleId <= 0 || normalizedName.Length == 0)
        {
            TempData["ManagementError"] =
                "Validation Role name is required after trim.";
            PreserveEditor(roleId, normalizedName, isAudited);
            return RedirectToRoleIndex(applicationId, EditorFragment(roleId));
        }

        if (actorEmployeeNo is null)
        {
            TempData["ManagementError"] =
                "Current employee identity is unavailable.";
            PreserveEditor(roleId, normalizedName, isAudited);
            return RedirectToRoleIndex(applicationId, EditorFragment(roleId));
        }

        AdministrationResult result = await _roleHandler.SaveValidationRoleAsync(
            new SaveValidationRoleCommand(
                applicationId,
                roleId,
                normalizedName,
                isAudited,
                reactivate,
                actorEmployeeNo),
            cancellationToken);

        if (result.Succeeded)
        {
            TempData["ManagementSuccess"] = reactivate
                ? "Validation Role reactivated."
                : "Validation Role saved.";
            TempData["FocusTarget"] = result.EntityId is int entityId
                ? $"role-{entityId}"
                : "page-heading";
            return RedirectToRoleIndex(
                applicationId,
                result.EntityId is int savedId
                    ? $"role-{savedId}"
                    : "page-heading");
        }

        if (result.ErrorCode == "ROLE_REACTIVATION_REQUIRED" &&
            result.EntityId is int inactiveRoleId)
        {
            TempData["ManagementError"] =
                "ROLE_REACTIVATION_REQUIRED: An inactive Validation Role " +
                "already uses this name. Confirm reactivation to reuse its " +
                "existing ROLE_ID.";
            TempData["ReactivationRoleId"] = inactiveRoleId;
            PreserveEditor(roleId, normalizedName, isAudited);
            return RedirectToRoleIndex(applicationId, "role-reactivation");
        }

        TempData["ManagementError"] = result.ErrorCode switch
        {
            "ROLE_NAME_DUPLICATE" =>
                "ROLE_NAME_DUPLICATE: Another active Validation Role already " +
                "uses that name after trim and case folding.",
            "ROLE_NOT_FOUND" =>
                "ROLE_NOT_FOUND: The Validation Role was not found in the " +
                "selected Application.",
            "ROLE_REACTIVATION_STALE" =>
                "ROLE_REACTIVATION_STALE: The confirmed inactive Validation " +
                "Role changed. Review the current row before trying again.",
            "ROLE_REACTIVATION_TARGET_REQUIRED" =>
                "ROLE_REACTIVATION_TARGET_REQUIRED: Confirm the exact inactive " +
                "Validation Role before reactivation.",
            "APPLICATION_NOT_FOUND" =>
                "APPLICATION_NOT_FOUND: The selected Application was not found.",
            _ => $"{result.ErrorCode ?? "ADMINISTRATION_ERROR"}: " +
                 "The Validation Role change was not saved."
        };
        bool targetMissing = result.ErrorCode is
            "ROLE_NOT_FOUND" or "APPLICATION_NOT_FOUND";
        if (!targetMissing)
        {
            PreserveEditor(roleId, normalizedName, isAudited);
        }

        return RedirectToRoleIndex(
            applicationId,
            targetMissing ? "page-heading" : EditorFragment(roleId));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy =
        RoleValidationAuthorizationPolicies.LocalItAdministration)]
    public async Task<IActionResult> Deactivate(
        int applicationId,
        int roleId,
        CancellationToken cancellationToken = default)
    {
        string? actorEmployeeNo = GetActorEmployeeNo();
        if (applicationId <= 0 || roleId <= 0)
        {
            TempData["ManagementError"] = "Validation Role ID is invalid.";
            return RedirectToRoleIndex(applicationId, "page-heading");
        }

        if (actorEmployeeNo is null)
        {
            TempData["ManagementError"] =
                "Current employee identity is unavailable.";
            return RedirectToRoleIndex(applicationId, $"role-{roleId}");
        }

        AdministrationResult result =
            await _roleHandler.DeactivateValidationRoleAsync(
                new DeactivateValidationRoleCommand(
                    applicationId,
                    roleId,
                    actorEmployeeNo),
                cancellationToken);
        if (result.Succeeded)
        {
            TempData["ManagementSuccess"] = "Validation Role deactivated.";
            TempData["FocusTarget"] = $"role-{roleId}";
        }
        else
        {
            TempData["ManagementError"] = result.ErrorCode switch
            {
                "ROLE_HAS_ACTIVE_DEPENDENCIES" =>
                    "ROLE_HAS_ACTIVE_DEPENDENCIES: Remove each active Owner " +
                    "assignment or Source Mapping before deactivation.",
                "ROLE_NOT_FOUND" =>
                    "ROLE_NOT_FOUND: The Validation Role was not found in the " +
                    "selected Application.",
                "APPLICATION_NOT_FOUND" =>
                    "APPLICATION_NOT_FOUND: The selected Application was not found.",
                _ => $"{result.ErrorCode ?? "ADMINISTRATION_ERROR"}: " +
                     "The Validation Role was not deactivated."
            };
        }

        return RedirectToRoleIndex(
            applicationId,
            result.ErrorCode is "ROLE_NOT_FOUND" or "APPLICATION_NOT_FOUND"
                ? "page-heading"
                : $"role-{roleId}");
    }

    private string? GetActorEmployeeNo()
    {
        string? value = User.FindFirstValue(
            RoleValidationAuthenticationDefaults.EmployeeNoClaimType);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private int? ReadTempDataInt(string tempDataName)
    {
        object? value = TempData[tempDataName];
        return value switch
        {
            int integer => integer,
            string text when int.TryParse(text, out int parsed) => parsed,
            _ => null
        };
    }

    private bool ReadTempDataBool(string tempDataName)
    {
        object? value = TempData[tempDataName];
        return value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out bool parsed) => parsed,
            _ => false
        };
    }

    private void PreserveEditor(
        int? roleId,
        string roleName,
        bool isAudited)
    {
        TempData["EditorRoleId"] = roleId;
        TempData["EditorRoleName"] = roleName;
        TempData["EditorIsAudited"] = isAudited;
    }

    private static string EditorFragment(int? roleId)
    {
        return roleId is int editorRoleId && editorRoleId > 0
            ? $"role-editor-{editorRoleId}"
            : "role-editor-new";
    }

    private RedirectToActionResult RedirectToRoleIndex(
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
}
