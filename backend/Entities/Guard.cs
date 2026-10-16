namespace backend.Entities;

// Shared argument validation for entity factories and ApplySync methods.
internal static class Guard
{
    public static void NotEmpty(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException($"{name} cannot be empty.");
    }

    // Required text: trimmed, non-blank, within the column length.
    public static string Text(string? value, int maxLength, string name)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) throw new ArgumentException($"{name} cannot be empty.");
        if (text.Length > maxLength) throw new ArgumentException($"{name} cannot exceed {maxLength} characters.");
        return text;
    }

    public static string? OptionalText(string? value, int maxLength, string name)
        => string.IsNullOrWhiteSpace(value) ? null : Text(value, maxLength, name);

    public static void Defined<T>(T value, string name) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentException($"Invalid {name}.");
    }

    // double.NaN and infinity pass naive "> 0" checks, so finiteness is checked explicitly.
    public static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentException($"{name} must be greater than 0.");
    }

    public static void Positive(double? value, string name)
    {
        if (value is not null) Positive(value.Value, name);
    }

    public static void InRange(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentException($"{name} must be between {min} and {max}.");
    }

    public static void InRange(double? value, double min, double max, string name)
    {
        if (value is not null) InRange(value.Value, min, max, name);
    }

    public static void NonNegative(int value, string name)
    {
        if (value < 0) throw new ArgumentException($"{name} cannot be negative.");
    }
}
