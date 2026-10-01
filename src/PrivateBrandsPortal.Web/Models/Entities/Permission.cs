namespace PrivateBrandsPortal.Web.Models.Entities;

public sealed class Permission
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public sealed class AppUserPermission
{
    public int AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;
    public int PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}
