using System.Globalization;
using System.Text;
using AxisApp.Localization;
using AxisApp.Models;
using Microsoft.Maui.Storage;
using SkiaSharp;

namespace AxisApp.Services;

/// <summary>Export for the Stats tab (POSSIBLE_FEATURES.md's v2 "Export" idea) — CSV is the
/// group's whole raw expense history (general-purpose, not Stats-specific: date/description/
/// category/amount/paid-by/settlement flag); PDF is a numbers-table snapshot of the same
/// aggregations GroupStatsViewModel builds for its charts, over whatever date range was selected
/// when the export was triggered. Both write to FileSystem.CacheDirectory and hand off to the OS
/// share sheet (Share.Default.RequestAsync) rather than a file picker — same "let the OS handle
/// where it ends up" approach ProfileViewModel/InviteToGroupViewModel already use for a text
/// share; this is this app's first *file* share, so worth a real on-device check before trusting
/// it blindly on both platforms.
///
/// PDF uses SkiaSharp's own SKDocument.CreatePdf rather than a new NuGet dependency — confirmed
/// present in the exact SkiaSharp 3.119.0 package this project already depends on (checked via the
/// installed package's own DLL, not assumed from docs), so this adds no new library, no licensing/
/// "gated free tier" question at all. Tables only, not redrawn charts — a deliberate v2 scope call
/// (see the chat design discussion), simpler and lower-risk than re-implementing bar/line drawing
/// against a raw SKCanvas.</summary>
public static class StatsExporter
{
    public static async Task<string> ExportExpensesCsvAsync(
        string groupName,
        IReadOnlyList<Expense> expenses,
        IReadOnlyDictionary<Guid, Member> membersById,
        IReadOnlyDictionary<Guid, string> aliases)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,Description,Category,Amount,Currency,Paid By,Settlement");
        foreach (var e in expenses.OrderByDescending(e => e.OccurredAt))
        {
            var paidBy = membersById.TryGetValue(e.PaidByMemberId, out var m) ? MemberDisplay.Name(m, aliases) : "?";
            sb.AppendLine(string.Join(",",
                CsvField(e.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd")),
                CsvField(e.Description),
                CsvField(CategoryDisplay.Label(e.Category)),
                CsvField(e.Amount.ToString("0.00", CultureInfo.InvariantCulture)),
                CsvField(e.Currency),
                CsvField(paidBy),
                CsvField(e.IsSettlement ? "Yes" : "No")));
        }

        var path = Path.Combine(FileSystem.CacheDirectory, $"{SanitizeFileName(groupName)}-expenses.csv");
        await File.WriteAllTextAsync(path, sb.ToString());
        return path;
    }

    private static string CsvField(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    public static Task<string> ExportStatsPdfAsync(
        string groupName,
        string rangeLabel,
        string groupSymbol,
        IReadOnlyList<Expense> expenses,
        IReadOnlyList<ExpenseShare> shares,
        IReadOnlyDictionary<Guid, Member> membersById,
        IReadOnlyDictionary<Guid, string> aliases) => Task.Run(() =>
    {
        var loc = LocalizationResourceManager.Instance;
        var nonSettlement = expenses.Where(e => !e.IsSettlement).ToList();

        var path = Path.Combine(FileSystem.CacheDirectory, $"{SanitizeFileName(groupName)}-stats.pdf");
        using var stream = new SKFileWStream(path);
        using var document = SKDocument.CreatePdf(stream);
        var writer = new PdfPageWriter(document, 595, 842); // A4 at 72dpi

        writer.Title($"{groupName} — {loc["GroupDetail_StatsTab"]}");
        writer.Subtitle(rangeLabel);
        writer.Spacer();

        writer.SectionHeader(loc["Stats_SpendByCategory"]);
        writer.Table(
            nonSettlement.GroupBy(e => e.Category)
                .Select(g => (CategoryDisplay.Label(g.Key), $"{groupSymbol}{g.Sum(e => e.AmountInGroupCurrency):0.00}"))
                .OrderByDescending(x => x.Item2));

        writer.SectionHeader(loc["Stats_CategoryFrequency"]);
        writer.Table(
            nonSettlement.GroupBy(e => e.Category)
                .Select(g => (CategoryDisplay.Label(g.Key), g.Count().ToString(CultureInfo.InvariantCulture)))
                .OrderByDescending(x => x.Item2));

        writer.SectionHeader(loc["Stats_SpendByMember"]);
        writer.Table(
            nonSettlement.GroupBy(e => e.PaidByMemberId)
                .Select(g => (MemberName(g.Key, membersById, aliases), $"{groupSymbol}{g.Sum(e => e.AmountInGroupCurrency):0.00}"))
                .OrderByDescending(x => x.Item2));

        writer.SectionHeader(loc["Stats_SettleUpCadence"]);
        writer.Table(
            expenses.Where(e => e.IsSettlement)
                .GroupBy(e => new DateTime(e.OccurredAt.ToLocalTime().Year, e.OccurredAt.ToLocalTime().Month, 1))
                .OrderBy(g => g.Key)
                .Select(g => (g.Key.ToString("MMM yyyy", CultureInfo.CurrentUICulture), g.Count().ToString(CultureInfo.InvariantCulture))));

        writer.Close();
        return path;
    });

    private static string MemberName(Guid memberId, IReadOnlyDictionary<Guid, Member> membersById, IReadOnlyDictionary<Guid, string> aliases) =>
        membersById.TryGetValue(memberId, out var member) ? MemberDisplay.Name(member, aliases) : "?";

    private static string SanitizeFileName(string name)
    {
        var cleaned = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        return string.IsNullOrWhiteSpace(cleaned) ? "group" : cleaned;
    }

    /// <summary>Minimal paginated text-table writer over an SKDocument — advances y per line,
    /// starts a new page when it runs out of room. Everything this export needs is plain rows of
    /// text, so this is deliberately not a general PDF layout engine.</summary>
    private sealed class PdfPageWriter
    {
        private readonly SKDocument document;
        private readonly float pageWidth;
        private readonly float pageHeight;
        private readonly SKFont titleFont = new(SKTypeface.Default, 20);
        private readonly SKFont subtitleFont = new(SKTypeface.Default, 12);
        private readonly SKFont headerFont = new(SKTypeface.Default, 14);
        private readonly SKFont bodyFont = new(SKTypeface.Default, 11);
        private readonly SKPaint textPaint = new() { Color = SKColors.Black, IsAntialias = true };
        private const float MarginX = 40;
        private const float MarginTop = 50;
        private const float MarginBottom = 40;

        private SKCanvas canvas = null!;
        private float y;

        public PdfPageWriter(SKDocument document, float pageWidth, float pageHeight)
        {
            this.document = document;
            this.pageWidth = pageWidth;
            this.pageHeight = pageHeight;
            NewPage();
        }

        private void NewPage()
        {
            canvas = document.BeginPage(pageWidth, pageHeight);
            canvas.Clear(SKColors.White);
            y = MarginTop;
        }

        private void EnsureRoom(float lineHeight)
        {
            if (y + lineHeight <= pageHeight - MarginBottom) return;
            document.EndPage();
            NewPage();
        }

        public void Title(string text)
        {
            EnsureRoom(28);
            canvas.DrawText(text, MarginX, y, SKTextAlign.Left, titleFont, textPaint);
            y += 28;
        }

        public void Subtitle(string text)
        {
            EnsureRoom(20);
            canvas.DrawText(text, MarginX, y, SKTextAlign.Left, subtitleFont, textPaint);
            y += 20;
        }

        public void Spacer() => y += 12;

        public void SectionHeader(string text)
        {
            y += 10;
            EnsureRoom(22);
            canvas.DrawText(text, MarginX, y, SKTextAlign.Left, headerFont, textPaint);
            y += 22;
        }

        public void Table(IEnumerable<(string Label, string Value)> rows)
        {
            var any = false;
            foreach (var (label, value) in rows)
            {
                any = true;
                EnsureRoom(18);
                canvas.DrawText(label, MarginX, y, SKTextAlign.Left, bodyFont, textPaint);
                canvas.DrawText(value, pageWidth - MarginX, y, SKTextAlign.Right, bodyFont, textPaint);
                y += 18;
            }
            if (!any)
            {
                EnsureRoom(18);
                canvas.DrawText("—", MarginX, y, SKTextAlign.Left, bodyFont, textPaint);
                y += 18;
            }
        }

        public void Close()
        {
            document.EndPage();
            document.Close();
        }
    }
}
