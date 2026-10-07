using System.ComponentModel.DataAnnotations;
using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.ViewModels;

public sealed class UserEditInput
{
    [Range(1, int.MaxValue)] public int Id { get; set; }
    [Required] public DateTimeOffset? Version { get; set; }
    [Required, StringLength(200), Display(Name="Display Name")] public string DisplayName { get; set; } = "";
    [EnumDataType(typeof(AppRole))] public AppRole Role { get; set; }
    [Display(Name = "Active")] public bool IsActive { get; set; }
    public List<int> PermissionIds { get; set; } = [];
}
public sealed class UserCreateInput
{
    [Required, StringLength(256), RegularExpression(@"[^\s\\/]+\\[^\s\\/]+", ErrorMessage="Use DOMAIN\\username."), Display(Name="Domain Login")]
    public string DomainLogin { get; set; } = "";
    [Required, StringLength(200), Display(Name="Display Name")] public string DisplayName { get; set; } = "";
    [Required, EnumDataType(typeof(AppRole))] public AppRole? Role { get; set; } = AppRole.ProjectManager;
    [Display(Name="Active")] public bool IsActive { get; set; } = true;
    public List<int> PermissionIds { get; set; } = [];
}
public sealed class UserCreatePage
{
    public UserCreateInput Input { get; set; } = new();
    public bool CanAssignSuperAdmin { get; init; }
    public IReadOnlyList<PermissionOption> Permissions { get; init; } = [];
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
