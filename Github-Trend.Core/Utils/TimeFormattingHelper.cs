using System;
using Github_Trend.Localization;

namespace Github_Trend.Utils;

public static class TimeFormattingHelper
{
    public static string FormatLastUpdatedBadge(string? updatedAt)
    {
        if (!DateTimeOffset.TryParse(updatedAt, out var updated))
            return Localization.Localization.Instance.GetString(
                nameof(LocalizationService.UpdatedUnknown)
            );

        var delta = DateTimeOffset.UtcNow - updated.ToUniversalTime();
        if (delta.TotalMinutes < 60)
            return delta.TotalMinutes < 1
                ? Localization.Localization.Instance.GetString(
                    nameof(LocalizationService.UpdatedJustNow)
                )
                : Localization.Localization.Instance.GetString(
                    Math.Max(1, (int)delta.TotalMinutes) == 1
                        ? nameof(LocalizationService.UpdatedMinutesAgoOne)
                        : nameof(LocalizationService.UpdatedMinutesAgoMany),
                    Math.Max(1, (int)delta.TotalMinutes)
                );

        if (delta.TotalHours < 24)
            return delta.TotalHours < 2
                ? Localization.Localization.Instance.GetString(
                    nameof(LocalizationService.UpdatedHoursAgoOne)
                )
                : Localization.Localization.Instance.GetString(
                    nameof(LocalizationService.UpdatedHoursAgoMany),
                    Math.Max(1, (int)delta.TotalHours)
                );

        if (delta.TotalDays < 7)
            return delta.TotalDays < 2
                ? Localization.Localization.Instance.GetString(
                    nameof(LocalizationService.UpdatedYesterday)
                )
                : Localization.Localization.Instance.GetString(
                    Math.Max(1, (int)delta.TotalDays) == 1
                        ? nameof(LocalizationService.UpdatedDaysAgoOne)
                        : nameof(LocalizationService.UpdatedDaysAgoMany),
                    Math.Max(1, (int)delta.TotalDays)
                );

        if (delta.TotalDays < 30)
            return Localization.Localization.Instance.GetString(
                nameof(LocalizationService.UpdatedWeeksAgo),
                Math.Max(1, (int)Math.Round(delta.TotalDays / 7d))
            );

        if (delta.TotalDays < 365)
            return Localization.Localization.Instance.GetString(
                nameof(LocalizationService.UpdatedMonthsAgo),
                Math.Max(1, (int)Math.Round(delta.TotalDays / 30d))
            );

        return Localization.Localization.Instance.GetString(
            nameof(LocalizationService.UpdatedYearsAgo),
            Math.Max(1, (int)Math.Round(delta.TotalDays / 365d))
        );
    }
}
