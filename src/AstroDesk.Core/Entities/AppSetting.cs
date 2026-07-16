namespace AstroDesk.Core.Entities;

public sealed class AppSetting : EntityBase
{
    private AppSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
        ValueType = "string";
    }

    public AppSetting(string key, string value, string valueType = "string", string? category = null)
    {
        Key = RequiredText(key, nameof(key), 200);
        Value = value ?? throw new ArgumentNullException(nameof(value));
        ValueType = RequiredText(valueType, nameof(valueType), 100);
        Category = OptionalText(category, nameof(category), 100);
    }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public string ValueType { get; private set; }

    public string? Category { get; private set; }

    public void SetValue(
        string value,
        string valueType = "string",
        string? category = null,
        DateTimeOffset? timestamp = null)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        ValueType = RequiredText(valueType, nameof(valueType), 100);
        Category = OptionalText(category, nameof(category), 100);
        MarkUpdated(timestamp);
    }

    private static string RequiredText(string value, string paramName, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Value cannot exceed {maxLength} characters.");
        }

        return trimmed;
    }

    private static string? OptionalText(string? value, string paramName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return RequiredText(value, paramName, maxLength);
    }
}
