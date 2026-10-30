using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace Vectorier.Services.Localization;

/// <summary>
/// XAML: <c>Text="{l:Tr 'Add Key'}"</c> shows the text in the current language and follows language changes.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Observe(Key).ToBinding();
}

/// <summary>Enum values shown in lists (ComboBoxes): their name in the current language.</summary>
public sealed class EnumTrConverter : IValueConverter
{
    public static readonly EnumTrConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Enum e ? Loc.T(Humanize(e.ToString())) : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>"FastSlideOff" → "Fast Slide Off" (the English text and the translation key).</summary>
    public static string Humanize(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1]))))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>Bound value inserted into a translated format: <c>Converter={x:Static l:FormatTr.Instance}, ConverterParameter='{0} element(s)'</c>.</summary>
public sealed class FormatTrConverter : IValueConverter
{
    public static readonly FormatTrConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => parameter is string format ? Loc.F(format.StartsWith("{}", StringComparison.Ordinal) ? format[2..] : format, value) : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Text of an enum value (the DataContext) in the current language; follows language changes.</summary>
public sealed class EnumLabel : Avalonia.Controls.TextBlock
{
    protected override Type StyleKeyOverride => typeof(Avalonia.Controls.TextBlock);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Refresh();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Loc.LanguageChanged += Refresh;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Loc.LanguageChanged -= Refresh;
    }

    private void Refresh() => Text = EnumTrConverter.Instance.Convert(DataContext, typeof(string), null, CultureInfo.CurrentCulture) as string;
}
