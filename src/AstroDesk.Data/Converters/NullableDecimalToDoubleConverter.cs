using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AstroDesk.Data.Converters;

internal sealed class NullableDecimalToDoubleConverter()
    : ValueConverter<decimal?, double?>(
        value => value.HasValue ? decimal.ToDouble(value.Value) : null,
        value => value.HasValue ? Convert.ToDecimal(value.Value) : null);
