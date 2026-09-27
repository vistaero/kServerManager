using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Markup;
using kServerManager.Core;

namespace kServerManager.Properties;

public static class LocalizedStrings
{
    public static string Get(string key, params object?[] arguments) =>
        Core.Localization.Get(key, arguments);
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    public TextExtension(string key) => Key = key;

    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider) => LocalizedStrings.Get(Key);
}

public sealed class ServerJarDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ServerJarInfo jar)
            return string.Empty;

        string fileName = Path.GetFileName(jar.Path);
        return jar.MinimumJavaMajor is int version
            ? LocalizedStrings.Get("JarDisplayWithJava", fileName, version)
            : LocalizedStrings.Get("JarDisplayJavaUnknown", fileName);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
