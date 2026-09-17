using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PrivateBrandsPortal.Web.Data;

// SQL datetimeoffset preserves the instant; normalize all persisted/read values to UTC.
public sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
    value => value.ToUniversalTime(), value => value.ToUniversalTime());
