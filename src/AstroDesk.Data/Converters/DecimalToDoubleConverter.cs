using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AstroDesk.Data.Converters;

/// <summary>
/// SQLite has no native decimal storage class. Values used by AstroDesk are
/// measurements rather than financial amounts, so REAL provides the required
/// query support and precision.
/// </summary>
internal sealed class DecimalToDoubleConverter()
    : ValueConverter<decimal, double>(
        value => decimal.ToDouble(value),
        value => Convert.ToDecimal(value));
