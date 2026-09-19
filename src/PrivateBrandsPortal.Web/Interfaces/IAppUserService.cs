using PrivateBrandsPortal.Web.Models.Entities;
namespace PrivateBrandsPortal.Web.Interfaces;
public interface IAppUserService
{
    Task<AppUser> GetCurrentAsync(CancellationToken cancellationToken = default);
}

