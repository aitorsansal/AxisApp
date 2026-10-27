using System.Globalization;
using System.Text.RegularExpressions;

namespace AxisApp.Models;

public enum AmountInputError { None, Invalid, TooLarge }

public static class AmountInput
{
    public const decimal MaximumAmount = 9999999999.99m;

    public static AmountInputError Parse(string? text, out decimal amount)
    {
        amount = 0;
        var normalized = (text ?? "").Trim().Replace(',', '.');
        if (!Regex.IsMatch(normalized, @"^(?:[0-9]+(?:\.[0-9]{1,2})?|\.[0-9]{1,2})$")) return AmountInputError.Invalid;
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed > MaximumAmount) return AmountInputError.TooLarge;
        if (parsed <= 0) return AmountInputError.Invalid;
        amount = parsed;
        return AmountInputError.None;
    }

    public static bool TryParse(string? text, out decimal amount) => Parse(text, out amount) == AmountInputError.None;

    public static string MessageKey(string? text) => Parse(text, out _) == AmountInputError.TooLarge
        ? "AmountInput_TooLarge" : "AmountInput_Invalid";
}
