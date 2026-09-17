namespace PrivateBrandsPortal.Web.Interfaces;

public interface ICurrentUserService
{
    string? DomainLogin { get; }
    bool IsAuthenticated { get; }
    string DisplayName { get; }
    string RoleLabel { get; }
}
