using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
namespace PrivateBrandsPortal.Web.Services;

public sealed class AppUserService(ApplicationDbContext db, ICurrentUserService current,
    IWebHostEnvironment environment, TimeProvider clock, IHttpContextAccessor accessor,
    ILogger<AppUserService> logger) : IAppUserService
{
    public async Task<AppUser> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (accessor.HttpContext?.Items["PortalAppUser"] is AppUser cached) return cached;
        var login = current.DomainLogin;
        if (!current.IsAuthenticated || string.IsNullOrWhiteSpace(login) || login.Length > 256)
            throw new PortalAccessException();
        var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.DomainLogin == login, cancellationToken);
        if (user is null)
        {
            if (!environment.IsDevelopment()) throw new PortalAccessException();
            var now = clock.GetUtcNow();
            user = new AppUser { DomainLogin = login, DisplayName = login.Length <= 200 ? login : login[..200],
                Role = AppRole.ProjectManager, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.AppUsers.Add(user);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Created development application profile {UserId}.", user.Id);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                db.Entry(user).State = EntityState.Detached;
                user = await db.AppUsers.AsNoTracking().SingleAsync(x => x.DomainLogin == login, cancellationToken);
            }
        }
        if (!user.IsActive) throw new PortalAccessException();
        if (accessor.HttpContext is { } context) context.Items["PortalAppUser"] = user;
        return user;
    }
}
