using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AstroDesk.Data.Converters;

internal sealed class TimeSpanToTicksConverter()
    : ValueConverter<TimeSpan, long>(
        value => value.Ticks,
        value => TimeSpan.FromTicks(value));
