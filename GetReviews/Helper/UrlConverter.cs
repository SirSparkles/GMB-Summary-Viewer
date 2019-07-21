using System;
using System.Windows.Data;

namespace GetReviews
{
    internal class UrlConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value != null && value.ToString().Length > 0)
            {
                return "click";
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            Uri u = new Uri((string) value ?? string.Empty);
            return u;
        }
    }
}