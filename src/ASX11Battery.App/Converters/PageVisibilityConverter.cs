using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ASX11Battery.App.Converters;

/// <summary>Converts a boolean to Visibility (Visible when true, Collapsed when false).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}