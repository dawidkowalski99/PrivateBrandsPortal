using PrivateBrandsPortal.Web.Models.Enums;

namespace PrivateBrandsPortal.Web.Models.Entities;

public sealed class Country
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Code { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UnixEpoch;
    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
