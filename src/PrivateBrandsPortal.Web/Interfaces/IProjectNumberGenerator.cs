namespace PrivateBrandsPortal.Web.Interfaces;

public interface IProjectNumberGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken = default);
}
