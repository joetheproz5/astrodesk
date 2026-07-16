using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AstroDesk.Data.Converters;

internal sealed class NullableTimeSpanToTicksConverter()
    : ValueConverter<TimeSpan?, long?>(
        value => value.HasValue ? value.Value.Ticks : null,
        value => value.HasValue ? TimeSpan.FromTicks(value.Value) : null);
