using ClipboardPal.Core.Models;

namespace ClipboardPal.Core.Services;

public static class HistoryRetentionHelper
{
    public static DateTime? Cutoff(HistoryRetention retention, DateTime now) => retention switch
    {
        HistoryRetention.Day => now.AddDays(-1),
        HistoryRetention.Week => now.AddDays(-7),
        HistoryRetention.Month => now.AddMonths(-1),
        HistoryRetention.ThreeMonths => now.AddMonths(-3),
        HistoryRetention.SixMonths => now.AddMonths(-6),
        HistoryRetention.Year => now.AddYears(-1),
        _ => null
    };

    public static IEnumerable<ClipItem> Expired(IEnumerable<ClipItem> items, HistoryRetention retention, DateTime now)
    {
        var cutoff = Cutoff(retention, now);
        if (cutoff is null) yield break;
        foreach (var item in items)
        {
            if (!item.IsPinned && item.CreatedAt < cutoff.Value && !item.IsInTrash)
                yield return item;
        }
    }
}
