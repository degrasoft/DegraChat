using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace DegraChat.App.Converters;

/// <summary>
/// Inverts a boolean value.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Returns true if the value equals the parameter.
/// </summary>
public class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;
        
        var enumValue = value.ToString();
        var targetValue = parameter.ToString();
        
        if (enumValue == null || targetValue == null)
            return false;
        
        return string.Equals(enumValue, targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null)
        {
            var str = parameter.ToString();
            if (str != null && targetType.IsEnum)
                return Enum.Parse(targetType, str);
        }
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Returns true if the value does NOT equal the parameter.
/// </summary>
public class EnumNotEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return true;
        
        var enumValue = value.ToString();
        var targetValue = parameter.ToString();
        
        if (enumValue == null || targetValue == null)
            return true;
        
        return !string.Equals(enumValue, targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Returns true if the string is not null or empty.
/// </summary>
public class StringNotNullOrEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
            return !string.IsNullOrEmpty(s);
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Returns the first character of a string.
/// </summary>
public class FirstCharConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s.Length > 0)
            return s[0].ToString();
        return "?";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Converts a ConnectionState to a color string.
/// </summary>
public class ConnectionStateToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DegraChat.Core.Models.ConnectionState state)
        {
            return state switch
            {
                DegraChat.Core.Models.ConnectionState.Connected => "#00C896",
                DegraChat.Core.Models.ConnectionState.Connecting => "#FFB84D",
                DegraChat.Core.Models.ConnectionState.Reconnecting => "#FFB84D",
                DegraChat.Core.Models.ConnectionState.Error => "#FF5C5C",
                _ => "#777777"
            };
        }
        return "#777777";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Converts a ConnectionState to a display string.
/// </summary>
public class ConnectionStateToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DegraChat.Core.Models.ConnectionState state)
        {
            return state switch
            {
                DegraChat.Core.Models.ConnectionState.Connected => "Подключено",
                DegraChat.Core.Models.ConnectionState.Connecting => "Подключение...",
                DegraChat.Core.Models.ConnectionState.Reconnecting => "Переподключение...",
                DegraChat.Core.Models.ConnectionState.Error => "Ошибка",
                DegraChat.Core.Models.ConnectionState.Disconnected => "Отключено",
                _ => state.ToString()
            };
        }
        return "Отключено";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// Converts a ChatPlatform to its accent color.
/// </summary>
public class PlatformToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DegraChat.Core.Models.ChatPlatform platform)
        {
            return platform switch
            {
                DegraChat.Core.Models.ChatPlatform.Twitch => "#9146FF",
                DegraChat.Core.Models.ChatPlatform.GoodGame => "#00CC00",
                DegraChat.Core.Models.ChatPlatform.Kick => "#53FC18",
                DegraChat.Core.Models.ChatPlatform.VKPlay => "#0077FF",
                DegraChat.Core.Models.ChatPlatform.YouTube => "#FF0000",
                _ => "#777777"
            };
        }
        return "#777777";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
