using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PrivateBrandsPortal.Web.Data;
using PrivateBrandsPortal.Web.Interfaces;

namespace PrivateBrandsPortal.Web.Services;

public sealed class ProjectNumberGenerator(ApplicationDbContext db, TimeProvider clock) : IProjectNumberGenerator
{
    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        // Materialize directly: NEXT VALUE FOR cannot be wrapped in a SQL subquery.
        // SQL Server allocates atomically across all app instances. Rollbacks may leave gaps.
        var values = await db.Database.SqlQueryRaw<long>(
            "SELECT NEXT VALUE FOR [dbo].[ProjectNumberSequence] AS [Value]").ToListAsync(cancellationToken);
        return Format(clock.GetUtcNow().Year, values.Single());
    }

    public static string Format(int year, long number)
    {
        if (year is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(year));
        if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
        return string.Create(CultureInfo.InvariantCulture, $"PB-{year:D4}-{number:D4}");
    }
}
