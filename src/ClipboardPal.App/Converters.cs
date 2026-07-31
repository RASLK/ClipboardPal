using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ClipboardPal;

public sealed class ImagePathToBitmapConverter : IValueConverter
{
    // Cards are 168 logical px wide; decode to 2x for HiDPI instead of loading full-size bitmaps.
    private const int ThumbWidth = 336;
    private const int CacheLimit = 128;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, (DateTime WriteTime, Bitmap Bitmap)> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
            return null;
        try
        {
            var writeTime = File.GetLastWriteTimeUtc(path);
            lock (Gate)
            {
                if (Cache.TryGetValue(path, out var hit) && hit.WriteTime == writeTime)
                    return hit.Bitmap;
            }

            using var stream = File.OpenRead(path);
            var bitmap = Bitmap.DecodeToWidth(stream, ThumbWidth);

            lock (Gate)
            {
                // Evicted bitmaps are left to GC: visible Image controls may still reference them.
                if (Cache.Count >= CacheLimit)
                    Cache.Clear();
                Cache[path] = (writeTime, bitmap);
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RelativeTimeConverter : IValueConverter
{
    /// <summary>Set at startup so relative times follow the UI language.</summary>
    public static Func<string, string>? Localize { get; set; }

    private static string L(string key, string fallback) => Localize?.Invoke(key) ?? fallback;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dt)
            return string.Empty;

        var delta = DateTime.Now - dt;
        if (delta.TotalMinutes < 1) return L("time.now", "just now");
        if (delta.TotalHours < 1) return string.Format(L("time.min", "{0} min ago"), (int)delta.TotalMinutes);
        if (dt.Date == DateTime.Today) return dt.ToString("HH:mm");
        if (dt.Date == DateTime.Today.AddDays(-1)) return string.Format(L("time.yesterday", "yesterday {0}"), dt.ToString("HH:mm"));
        return dt.ToString("dd.MM HH:mm");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class SlotVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EnumMatchConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter as string;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string name && targetType.IsEnum)
            return Enum.Parse(targetType, name);
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}

/// <summary>Binds ComboBox.SelectedIndex ↔ enum property.</summary>
public sealed class EnumIntConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum e ? System.Convert.ToInt32(e) : 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // A negative index means "nothing selected", not enum value -1.
        if (value is not int i || i < 0)
            return Avalonia.Data.BindingOperations.DoNothing;

        var type = ResolveEnumType(targetType, parameter);
        return type is not null && type.IsEnum
            ? Enum.ToObject(type, i)
            : Avalonia.Data.BindingOperations.DoNothing;
    }

    private static Type? ResolveEnumType(Type targetType, object? parameter)
    {
        if (targetType.IsEnum) return targetType;
        if (parameter is Type t) return t;
        if (parameter is string name)
        {
            return Type.GetType($"ClipboardPal.Core.Models.{name}, ClipboardPal.Core")
                   ?? Type.GetType(name);
        }
        return null;
    }
}
