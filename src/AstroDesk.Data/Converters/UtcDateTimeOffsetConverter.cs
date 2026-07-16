using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AstroDesk.Data.Converters;

/// <summary>
/// Stores timestamps as UTC ticks so SQLite can sort and compare them reliably.
/// </summary>
internal sealed class UtcDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks,
        value => new DateTimeOffset(value, TimeSpan.Zero));
