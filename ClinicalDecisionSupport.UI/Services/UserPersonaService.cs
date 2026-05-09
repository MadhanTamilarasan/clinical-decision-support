using ClinicalDecisionSupport.UI.Models;
using System.Security.Claims;

namespace ClinicalDecisionSupport.UI.Services;

public class UserPersonaService
{
    public UserRole CurrentRole { get; private set; } = UserRole.Physician;

    public UserRole? AssignedRole { get; private set; }

    public string UserName { get; private set; } = "Unknown User";

    public event Action? OnRoleChanged;

    public void InitializeFromClaims(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return;

        UserName = user.FindFirst("name")?.Value
                   ?? user.FindFirst(ClaimTypes.Name)?.Value
                   ?? user.Identity.Name
                   ?? "Unknown User";

        var roles = user.FindAll(ClaimTypes.Role)
                        .Concat(user.FindAll("roles"))
                        .Select(c => c.Value)
                        .ToList();

        if (roles.Any(r => r.Equals("Physician", StringComparison.OrdinalIgnoreCase)))
            AssignedRole = UserRole.Physician;
        else if (roles.Any(r => r.Equals("Nurse", StringComparison.OrdinalIgnoreCase)))
            AssignedRole = UserRole.Nurse;
        else if (roles.Any(r => r.Equals("Specialist", StringComparison.OrdinalIgnoreCase)))
            AssignedRole = UserRole.Specialist;

        if (AssignedRole.HasValue)
            CurrentRole = AssignedRole.Value;
    }

    public void SetRole(UserRole role)
    {
        if (CurrentRole != role)
        {
            CurrentRole = role;
            OnRoleChanged?.Invoke();
        }
    }

    public string GetRoleBadgeColor() => CurrentRole switch
    {
        UserRole.Physician => "#0d6efd",
        UserRole.Nurse => "#198754",
        UserRole.Specialist => "#6f42c1",
        _ => "#6c757d"
    };
}
