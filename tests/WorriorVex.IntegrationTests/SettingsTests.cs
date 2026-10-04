using WorriorVex.Application.Settings;
using WorriorVex.Application.Storage;

namespace WorriorVex.IntegrationTests;

public class SettingsTests
{
    [Fact]
    public async Task Settings_are_kept_in_the_data_folder_clamped_and_read_back_at_the_next_start()
    {
        using var data = new TempDataDirectory();
        AppSettings? announced = null;
        await using (var first = await TestApp.StartAsync(data.Path))
        {
            var settings = first.Get<ISettingsService>();
            Assert.Equal(new AppSettings(), settings.Current);
            settings.Changed += s => announced = s;

            await settings.SaveAsync(new AppSettings { Theme = ThemeSetting.Dark, EditorFontSize = 99, AutosaveDelayMilliseconds = 10, ConfirmDeletion = true, LastNotebookId = Guid.Empty });

            Assert.Equal(ThemeSetting.Dark, settings.Current.Theme);
            Assert.Equal(AppSettings.MaxFontSize, settings.Current.EditorFontSize);
            Assert.Equal(AppSettings.MinAutosaveMilliseconds, settings.Current.AutosaveDelayMilliseconds);
            Assert.Equal(settings.Current, announced);
            Assert.True(File.Exists(Path.Combine(first.Get<IApplicationDataPathProvider>().DataDirectory, "settings.json")));
        }

        await using var second = await TestApp.StartAsync(data.Path);
        var reloaded = second.Get<ISettingsService>().Current;
        Assert.Equal(ThemeSetting.Dark, reloaded.Theme);
        Assert.True(reloaded.ConfirmDeletion);
        Assert.Equal(Guid.Empty, reloaded.LastNotebookId);
    }

    [Fact]
    public async Task A_broken_settings_file_means_defaults_not_a_failure_to_start()
    {
        using var data = new TempDataDirectory();
        await File.WriteAllTextAsync(Path.Combine(data.Path, "settings.json"), "{ not json");

        await using var app = await TestApp.StartAsync(data.Path);

        Assert.Equal(new AppSettings(), app.Get<ISettingsService>().Current);
        await app.Get<ISettingsService>().SaveAsync(new AppSettings { SpellCheck = false });
        Assert.False(app.Get<ISettingsService>().Current.SpellCheck);
    }

    [Fact]
    public async Task An_older_file_with_the_old_default_order_moves_to_the_new_default_once()
    {
        using var data = new TempDataDirectory();
        var path = Path.Combine(data.Path, "settings.json");
        await File.WriteAllTextAsync(path, """{ "noteSort": "Updated", "confirmDeletion": false, "editorFontSize": 17 }""");

        await using (var app = await TestApp.StartAsync(data.Path))
        {
            var settings = app.Get<WorriorVex.Application.Settings.ISettingsService>();
            Assert.Equal(WorriorVex.Application.Settings.NoteSort.Added, settings.Current.NoteSort);
            Assert.True(settings.Current.ConfirmDeletion);
            Assert.Equal(17, settings.Current.EditorFontSize);
            Assert.Contains("\"settingsVersion\"", await File.ReadAllTextAsync(path));

            // A choice made now sticks, because the file carries the version from here on.
            await settings.SaveAsync(settings.Current with { NoteSort = WorriorVex.Application.Settings.NoteSort.Updated });
        }

        await using (var app = await TestApp.StartAsync(data.Path))
        {
            Assert.Equal(WorriorVex.Application.Settings.NoteSort.Updated, app.Get<WorriorVex.Application.Settings.ISettingsService>().Current.NoteSort);
        }
    }
}
