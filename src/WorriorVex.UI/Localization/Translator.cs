using System.Globalization;
using System.Reflection;
using System.Text.Json;
using WorriorVex.Application.Settings;

namespace WorriorVex.UI.Localization;

/// <summary>
/// Translates the words of the interface. The English text is the key, so the source reads as
/// English and a language file maps English to the translation; anything missing falls back to
/// English. The language follows Settings → Language, or the system language when that is empty.
/// </summary>
public sealed class Translator : IDisposable
{
    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
    [
        ("en", "English"),
        ("nl", "Nederlands"),
        ("pl", "Polski"),
        ("de", "Deutsch"),
    ];

    // The language the system had when the app started, before any setting changed the thread cultures.
    private static readonly string SystemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentCulture;

    private readonly ISettingsService _settings;
    private Dictionary<string, string> _map = [];

    public Translator(ISettingsService settings)
    {
        _settings = settings;
        Use(settings.Current.Language);
        settings.Changed += OnSettingsChanged;
    }

    /// <summary>The language in use: "en", "nl", "pl" or "de".</summary>
    public string Language { get; private set; } = "en";

    /// <summary>Raised when the language changed; the interface re-renders.</summary>
    public event Action? Changed;

    public string this[string text] => _map.TryGetValue(text, out var translated) ? translated : text;

    public string this[string text, params object?[] args] => string.Format(CultureInfo.CurrentCulture, this[text], args);

    /// <summary>The language code a setting resolves to.</summary>
    public static string Resolve(string? setting)
    {
        var code = string.IsNullOrWhiteSpace(setting) ? SystemLanguage : setting.Trim().ToLowerInvariant();
        return Languages.Any(l => l.Code == code) ? code : "en";
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        var code = Resolve(settings.Language);
        if (code != Language)
        {
            Use(settings.Language);
            Changed?.Invoke();
        }
    }

    private void Use(string? setting)
    {
        Language = Resolve(setting);
        _map = Language == "en" ? [] : Load(Language);
        // Dates and numbers follow the chosen language; with the system language, the system's own formats stay.
        var culture = string.IsNullOrWhiteSpace(setting)
            ? SystemCulture
            : CultureInfo.GetCultureInfo(Language switch { "nl" => "nl-NL", "pl" => "pl-PL", "de" => "de-DE", _ => "en-GB" });
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
    }

    private static Dictionary<string, string> Load(string code)
    {
        var assembly = typeof(Translator).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($"i18n.{code}.json", StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return [];
        }

        using var stream = assembly.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;
}
