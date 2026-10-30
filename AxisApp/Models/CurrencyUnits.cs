using System.Globalization;

namespace AxisApp.Models;

/// <summary>Decimals per currency. Mirrors public.currency_minor_units() in
/// supabase/zero_decimal_currencies.sql and webapp/src/lib/currencyUnits.ts; a regression test
/// fails if the SQL list and this one drift. HUF and IDR stay at two decimals on purpose.</summary>
public static class CurrencyUnits
{
    public static readonly IReadOnlyList<string> ZeroDecimal = ["ISK", "JPY", "KRW"];

    public static int MinorUnits(string? currency) => currency is not null && ZeroDecimal.Contains(currency) ? 0 : 2;

    public static int Factor(string? currency) => MinorUnits(currency) == 0 ? 1 : 100;

    /// <summary>Integer minor units (cents; whole yen for JPY, KRW, ISK).</summary>
    public static long ToMinor(decimal amount, string? currency) =>
        decimal.ToInt64(decimal.Round(amount * Factor(currency), 0, MidpointRounding.AwayFromZero));

    public static decimal FromMinor(long minor, string? currency) => minor / (decimal)Factor(currency);

    /// <summary>Rounds to the currency's own decimals (2, or 0 for yen).</summary>
    public static decimal Round(decimal amount, string? currency) =>
        decimal.Round(amount, MinorUnits(currency), MidpointRounding.AwayFromZero);

    /// <summary>Custom numeric format string: "0.00", or "0" for a zero-decimal currency.</summary>
    public static string FormatString(string? currency) => MinorUnits(currency) == 0 ? "0" : "0.00";

    /// <summary>The amount as plain text with the currency's decimals, in the given culture
    /// (current culture when none is passed, like the "0.00" call sites this replaces).</summary>
    public static string Text(decimal amount, string? currency, IFormatProvider? provider = null) =>
        amount.ToString(FormatString(currency), provider ?? CultureInfo.CurrentCulture);
}
