using System.ComponentModel.DataAnnotations;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.ViewModels;

public sealed class UserEditInput
{
    [Range(1, int.MaxValue)] public int Id { get; set; }
    [Required] public DateTimeOffset? Version { get; set; }
    [EnumDataType(typeof(AppRole))] public AppRole Role { get; set; }
    [Display(Name = "Active")] public bool IsActive { get; set; }
    public List<int> PermissionIds { get; set; } = [];
}
public sealed record PermissionOption(int Id, string Name, bool IsActive);
public sealed record UserListItem(int Id, string DomainLogin, string DisplayName, AppRole Role, bool IsActive,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, List<string> Permissions);
public sealed record UserListPage(string? Search, IReadOnlyList<UserListItem> Users);
public sealed class UserEditPage
{
    public required UserEditInput Input { get; set; }
    public required string DomainLogin { get; init; }
    public required string DisplayName { get; init; }
    public bool CanAssignSuperAdmin { get; init; }
    public required IReadOnlyList<PermissionOption> Permissions { get; init; }
}
