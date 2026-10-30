using System.Globalization;
using System.Text.RegularExpressions;

namespace AxisApp.Models;

public enum AmountInputError { None, Invalid, TooLarge, NoDecimals }

/// <summary>Parses an amount typed in a given currency. Same contract as
/// webapp/src/lib/amountInput.ts; the shared cases are in tests/amount-input-cases.json. A currency
/// without decimals (JPY, KRW, ISK) rejects a decimal part with its own error rather than
/// rounding it, and "1.000" is rejected the same way (no thousands separators).</summary>
public static class AmountInput
{
    public const decimal MaximumAmount = 9999999999.99m;

    public static AmountInputError Parse(string? text, string? currency, out decimal amount)
    {
        amount = 0;
        var normalized = (text ?? "").Trim().Replace(',', '.');
        if (CurrencyUnits.MinorUnits(currency) == 0)
        {
            if (Regex.IsMatch(normalized, @"^(?:[0-9]+\.[0-9]+|\.[0-9]+)$")) return AmountInputError.NoDecimals;
            if (!Regex.IsMatch(normalized, @"^[0-9]+$")) return AmountInputError.Invalid;
        }
        else if (!Regex.IsMatch(normalized, @"^(?:[0-9]+(?:\.[0-9]{1,2})?|\.[0-9]{1,2})$"))
        {
            return AmountInputError.Invalid;
        }
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed > MaximumAmount) return AmountInputError.TooLarge;
        if (parsed <= 0) return AmountInputError.Invalid;
        amount = parsed;
        return AmountInputError.None;
    }

    public static bool TryParse(string? text, string? currency, out decimal amount) =>
        Parse(text, currency, out amount) == AmountInputError.None;

    public static string MessageKey(string? text, string? currency) => Parse(text, currency, out _) switch
    {
        AmountInputError.TooLarge => "AmountInput_TooLarge",
        AmountInputError.NoDecimals => "AmountInput_NoDecimals",
        _ => CurrencyUnits.MinorUnits(currency) == 0 ? "AmountInput_InvalidWhole" : "AmountInput_Invalid"
    };
}
