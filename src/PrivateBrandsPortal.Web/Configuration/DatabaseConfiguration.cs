using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
namespace PrivateBrandsPortal.Web.Configuration;

public static class DatabaseConfiguration
{
    public static IServiceCollection AddPortalDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connection = configuration.GetConnectionString("DefaultConnection");
            // Stage 1 does not query the database. Never invent a SQL instance.
            if (string.IsNullOrWhiteSpace(connection)) options.UseSqlServer();
            else options.UseSqlServer(connection);
        });
        return services;
    }
}
