using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorriorVex.Application.Notes;
using WorriorVex.Application.Search;
using WorriorVex.Application.Tags;
using WorriorVex.Application.Tree;
using WorriorVex.Infrastructure;
using WorriorVex.Infrastructure.Persistence;

// Usage: dotnet run -- [notes=10000] [dir=<folder>]
var notesWanted = args.Select(a => a.Split('=')).Where(p => p[0] == "notes").Select(p => int.Parse(p[1])).DefaultIfEmpty(10_000).First();
var directory = args.Select(a => a.Split('=')).Where(p => p[0] == "dir").Select(p => p[1]).DefaultIfEmpty(Path.Combine(Path.GetTempPath(), "worriorvex-bench")).First();
Directory.CreateDirectory(directory);

var services = new ServiceCollection();
services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
services.AddWorriorVexInfrastructure(directory);
await using var provider = services.BuildServiceProvider();

var startup = Stopwatch.StartNew();
await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
Console.WriteLine($"startup (migrations + index check): {startup.ElapsedMilliseconds} ms");

var notes = provider.GetRequiredService<INoteService>();
var notebooks = provider.GetRequiredService<INotebookService>();
var tree = provider.GetRequiredService<ITreeService>();
var tags = provider.GetRequiredService<ITagService>();
var search = provider.GetRequiredService<INoteSearchService>();

var existing = (await notes.ListAllAsync()).Count;
if (existing < notesWanted)
{
    var random = new Random(42);
    var words = "alpha budget cloud delta engine forest garden harbour island jungle kernel lantern market nebula orbit planet quartz river summit tunnel vector window yellow zenith architecture résumé quarterly plan meeting review sprint design database security network".Split(' ');
    string Sentence(int n) => string.Join(' ', Enumerable.Range(0, n).Select(_ => words[random.Next(words.Length)]));
    var fill = Stopwatch.StartNew();
    for (var nb = 0; existing < notesWanted; nb++)
    {
        var notebook = await notebooks.CreateAsync($"Notebook {nb + 1}");
        for (var f = 0; f < 10 && existing < notesWanted; f++)
        {
            var folder = await tree.CreateFolderAsync(notebook.Id, null, $"Folder {f + 1}");
            for (var i = 0; i < 100 && existing < notesWanted; i++)
            {
                var body = "<p>" + string.Join("</p><p>", Enumerable.Range(0, 6).Select(_ => Sentence(40))) + "</p>";
                var note = await notes.CreateAsync(notebook.Id, folder.Id, Sentence(4), body);
                if (i % 7 == 0)
                {
                    await tags.AddToNoteAsync(note.Id, words[random.Next(words.Length)]);
                }

                existing++;
            }
        }

        Console.WriteLine($"  filled {existing} notes ({fill.Elapsed.TotalSeconds:0}s)");
    }

    Console.WriteLine($"fill: {fill.Elapsed.TotalSeconds:0.0} s for {existing} notes");
}

async Task Time(string name, Func<Task<int>> action)
{
    // Warm once, then take the median of five.
    await action();
    var samples = new List<double>();
    var count = 0;
    for (var i = 0; i < 5; i++)
    {
        var watch = Stopwatch.StartNew();
        count = await action();
        samples.Add(watch.Elapsed.TotalMilliseconds);
    }

    samples.Sort();
    Console.WriteLine($"{name,-40} {samples[2],8:0.0} ms  ({count} rows)");
}

var all = await notes.ListAllAsync();
var first = all[0];
var firstNotebook = (await notebooks.ListAsync()).First(n => !n.IsInbox);
await Time("list all notes", async () => (await notes.ListAllAsync()).Count);
await Time("list one folder", async () => (await notes.ListAsync(first.NotebookId, first.ParentId)).Count);
await Time("list favorites", async () => (await notes.ListFavoritesAsync()).Count);
await Time("list recent", async () => (await notes.ListRecentAsync()).Count);
await Time("list loose ends", async () => (await notes.ListLooseEndsAsync()).Count);
await Time("list folders (tree)", async () => (await tree.ListFoldersAsync()).Count);
await Time("list tags", async () => (await tags.ListAsync()).Count);
await Time("open a note", async () => (await notes.GetAsync(first.Id)) is null ? 0 : 1);
await Time("search one word", async () => (await search.SearchAsync("budget")).Count);
await Time("search two words", async () => (await search.SearchAsync("budget plan")).Count);
await Time("search prefix", async () => (await search.SearchAsync("arch")).Count);
await Time("search scoped to notebook", async () => (await search.SearchAsync("budget", new SearchScope(firstNotebook.Id))).Count);
await Time("did you mean", async () => (await search.SuggestAsync("budgit")) is null ? 0 : 1);
await Time("save a note (same body)", async () => { await notes.UpdateAsync(first.Id, first.Title, (await notes.GetAsync(first.Id))!.Content); return 1; });
await Time("save a note (changed)", async () => { await notes.UpdateAsync(first.Id, first.Title, $"<p>edit {Guid.NewGuid()}</p>"); return 1; });
Console.WriteLine($"database: {new FileInfo(Path.Combine(directory, "worriorvex.db")).Length / 1024 / 1024} MB in {directory}");
