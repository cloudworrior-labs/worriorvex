using System.Text.Json;
using System.Text.RegularExpressions;
using WorriorVex.Application.Settings;
using WorriorVex.UI.Localization;

namespace WorriorVex.IntegrationTests;

public sealed partial class TranslationTests
{
    private static readonly string[] Codes = ["nl", "de", "pl"];

    private static Dictionary<string, string> Load(string code)
    {
        var assembly = typeof(Translator).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith($"i18n.{code}.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    [Fact]
    public void Every_language_translates_the_same_set_of_texts()
    {
        var sets = Codes.ToDictionary(c => c, c => Load(c).Keys.ToHashSet(StringComparer.Ordinal));
        foreach (var code in Codes)
        {
            Assert.True(sets[code].SetEquals(sets["nl"]), $"{code} differs from nl: {string.Join(" | ", sets[code].Except(sets["nl"]).Concat(sets["nl"].Except(sets[code])))}");
        }
    }

    [Fact]
    public void Placeholders_survive_translation()
    {
        foreach (var code in Codes)
        {
            foreach (var (english, translated) in Load(code))
            {
                var expected = Placeholder().Matches(english).Select(m => m.Value).ToHashSet();
                var actual = Placeholder().Matches(translated).Select(m => m.Value).ToHashSet();
                Assert.True(expected.SetEquals(actual), $"{code}: \"{english}\" -> \"{translated}\"");
                Assert.False(string.IsNullOrWhiteSpace(translated), $"{code}: \"{english}\" is empty");
            }
        }
    }

    [Fact]
    public async Task The_translator_follows_the_language_setting_and_falls_back_to_english()
    {
        using var data = new TempDataDirectory();
        await using var app = await TestApp.StartAsync(data.Path);
        var settings = app.Get<ISettingsService>();
        var translator = new Translator(settings);

        await settings.SaveAsync(settings.Current with { Language = "nl" });
        Assert.Equal("nl", translator.Language);
        Assert.Equal("Prullenbak", translator["Trash"]);
        Assert.Equal("Not translated", translator["Not translated"]);
        Assert.Equal("“Plan” is naar de prullenbak verplaatst.", translator["“{0}” was moved to the trash.", "Plan"]);

        await settings.SaveAsync(settings.Current with { Language = "xx" });
        Assert.Equal("en", translator.Language);
        Assert.Equal("Trash", translator["Trash"]);
    }

    [GeneratedRegex(@"\{\d\}")]
    private static partial Regex Placeholder();
}
