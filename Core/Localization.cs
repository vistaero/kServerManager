using System.Globalization;
using System.Resources;

namespace kServerManager.Core;

public static class Localization
{
    private static readonly ResourceManager ResourceManager = new(
        "kServerManager.Core.Resources",
        typeof(Localization).Assembly);

    public static string Get(string key, params object?[] arguments)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        if (culture.TwoLetterISOLanguageName == "es")
            culture = CultureInfo.GetCultureInfo("es-ES");

        string value = ResourceManager.GetString(key, culture) ?? key;
        return arguments.Length == 0 ? value : string.Format(culture, value, arguments);
    }
}
