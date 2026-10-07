namespace PrivateBrandsPortal.Web.Models.Entities;
public sealed class CustomerRejectionReason
{
    public int Id {get;set;}
    public required string Code {get;set;}
    public required string Name {get;set;}
    public string? Description {get;set;}
    public bool IsActive {get;set;} = true;
    public int DisplayOrder {get;set;}
    public DateTimeOffset CreatedAtUtc {get;set;}
    public DateTimeOffset UpdatedAtUtc {get;set;}
}
