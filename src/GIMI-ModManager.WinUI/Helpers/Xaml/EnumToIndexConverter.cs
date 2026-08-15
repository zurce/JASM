using Microsoft.UI.Xaml.Data;

namespace GIMI_ModManager.WinUI.Helpers.Xaml;

/// <summary>
/// Converts an enum value to/from its 0-based ordinal index, for binding a ComboBox
/// <c>SelectedIndex</c> to an enum-field-backed property whose enum values are defined in
/// ascending order (0,1,2,...).
/// </summary>
public class EnumToIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        if (value is null || !value.GetType().IsEnum)
            return -1;
        return (int)value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var index = value is int i ? i : -1;
        if (index < 0 || !targetType.IsEnum)
            return -1;
        var values = Enum.GetValues(targetType);
        return index < values.Length ? values.GetValue(index)! : index;
    }
}