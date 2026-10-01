namespace PrivateBrandsPortal.Web.Interfaces;

public interface IPermissionService
{
    Task<bool> HasAsync(string code, CancellationToken ct = default);
    Task RequireAsync(string code, CancellationToken ct = default);
}
