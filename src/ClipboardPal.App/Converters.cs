using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ClipboardPal;

public sealed class ImagePathToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
            return null;
        try
        {
            return new Bitmap(path);
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
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dt)
            return string.Empty;

        var delta = DateTime.Now - dt;
        if (delta.TotalMinutes < 1) return "только что";
        if (delta.TotalHours < 1) return $"{(int)delta.TotalMinutes} мин назад";
        if (dt.Date == DateTime.Today) return dt.ToString("HH:mm");
        if (dt.Date == DateTime.Today.AddDays(-1)) return $"вчера {dt:HH:mm}";
        return dt.ToString("dd.MM HH:mm");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.0;

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

public sealed class SelectedToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = value is true;
        // Accent vs card border — resolved at runtime from app resources if possible.
        return selected
            ? Avalonia.Media.Brush.Parse("#FF4C8DFF")
            : Avalonia.Media.Brush.Parse("#1FFFFFFF");
    }

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
        if (value is not int i)
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
