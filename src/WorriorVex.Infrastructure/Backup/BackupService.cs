using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorriorVex.Application.Backup;
using WorriorVex.Application.Storage;
using WorriorVex.Infrastructure.Persistence;

namespace WorriorVex.Infrastructure.Backup;

/// <summary>
/// A backup is a zip holding <c>worriorvex.db</c> (copied with SQLite's backup API, so it is consistent
/// even while the app runs), the <c>attachments/</c> folder and <c>manifest.json</c>.
/// </summary>
public sealed class BackupService(
    IDbContextFactory<WorriorVexDbContext> contextFactory,
    IApplicationDataPathProvider paths,
    DatabaseInitializer initializer,
    TimeProvider timeProvider,
    ILogger<BackupService> logger) : IBackupService
{
    public const string Format = "worriorvex-backup";
    public const int FormatVersion = 1;
    private const string DatabaseEntry = "worriorvex.db";
    private const string ManifestEntry = "manifest.json";
    private const string AttachmentsPrefix = "attachments/";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<string> CreateBackupAsync(string? destinationPath = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.BackupsDirectory);
        var now = timeProvider.GetUtcNow();
        var destination = destinationPath ?? Path.Combine(paths.BackupsDirectory, $"worriorvex-backup-{now.ToLocalTime():yyyyMMdd-HHmmss}.zip");
        var temporary = destination + ".partial";

        var manifest = await DescribeAsync(now, cancellationToken);
        var snapshot = Path.Combine(paths.BackupsDirectory, $"snapshot-{Guid.NewGuid():N}.db");
        try
        {
            await using (var source = new SqliteConnection($"Data Source={paths.DatabasePath}"))
            await using (var target = new SqliteConnection($"Data Source={snapshot}"))
            {
                await source.OpenAsync(cancellationToken);
                await target.OpenAsync(cancellationToken);
                source.BackupDatabase(target);
            }

            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={snapshot}"));
            await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
                await using (var stream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(stream, manifest, Json, cancellationToken);
                }

                zip.CreateEntryFromFile(snapshot, DatabaseEntry, CompressionLevel.Optimal);
                if (Directory.Exists(paths.AttachmentsDirectory))
                {
                    foreach (var attachment in Directory.EnumerateFiles(paths.AttachmentsDirectory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        zip.CreateEntryFromFile(attachment, AttachmentsPrefix + Path.GetFileName(attachment), CompressionLevel.Fastest);
                    }
                }
            }

            File.Move(temporary, destination, overwrite: true);
            logger.LogInformation("Backup written to {Path}: {Notes} notes, {Attachments} attachment files", destination, manifest.Notes, manifest.Attachments);
            return destination;
        }
        finally
        {
            File.Delete(snapshot);
            File.Delete(temporary);
        }
    }

    public async Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(paths.BackupsDirectory))
        {
            return [];
        }

        var backups = new List<BackupInfo>();
        foreach (var file in Directory.EnumerateFiles(paths.BackupsDirectory, "*.zip"))
        {
            backups.Add(await InspectAsync(file, cancellationToken));
        }

        return [.. backups.OrderByDescending(b => b.Manifest?.CreatedAt ?? DateTimeOffset.MinValue).ThenByDescending(b => b.Path)];
    }

    public async Task<BackupInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var problems = new List<string>();
        if (!File.Exists(path))
        {
            return new BackupInfo(path, 0, null, ["The file does not exist."]);
        }

        var size = new FileInfo(path).Length;
        BackupManifest? manifest = null;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifestEntry = zip.GetEntry(ManifestEntry);
            if (manifestEntry is null)
            {
                problems.Add("The file has no manifest, so it is not a WorriorVex backup.");
            }
            else
            {
                await using var stream = manifestEntry.Open();
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, Json, cancellationToken);
                if (manifest is null || manifest.Format != Format)
                {
                    problems.Add("The manifest is not one WorriorVex wrote.");
                    manifest = null;
                }
                else if (manifest.FormatVersion > FormatVersion)
                {
                    problems.Add($"The backup was made by a newer WorriorVex (backup format {manifest.FormatVersion}). Update WorriorVex to restore it.");
                }
            }

            var database = zip.GetEntry(DatabaseEntry);
            if (database is null)
            {
                problems.Add("The backup has no database.");
            }
            else if (manifest is not null && problems.Count == 0)
            {
                problems.AddRange(await CheckDatabaseAsync(database, cancellationToken));
            }
        }
        catch (InvalidDataException)
        {
            problems.Add("The file is not a zip archive.");
        }
        catch (JsonException)
        {
            problems.Add("The manifest could not be read.");
        }

        return new BackupInfo(path, size, manifest, problems);
    }

    public async Task<string> RestoreAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = await InspectAsync(path, cancellationToken);
        if (!info.CanRestore)
        {
            throw new BackupException(string.Join(" ", info.Problems));
        }

        var safety = await CreateBackupAsync(
            Path.Combine(paths.BackupsDirectory, $"worriorvex-before-restore-{timeProvider.GetUtcNow().ToLocalTime():yyyyMMdd-HHmmss}.zip"),
            cancellationToken);
        logger.LogInformation("Restoring backup {Path}; current data saved to {Safety}", path, safety);

        // Everything below replaces files the open database points at, so no connection may stay open.
        SqliteConnection.ClearAllPools();
        var staging = Path.Combine(paths.DataDirectory, $"restore-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            using (var zip = ZipFile.OpenRead(path))
            {
                zip.GetEntry(DatabaseEntry)!.ExtractToFile(Path.Combine(staging, DatabaseEntry), overwrite: true);
                var attachmentsStaging = Path.Combine(staging, "attachments");
                Directory.CreateDirectory(attachmentsStaging);
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(AttachmentsPrefix, StringComparison.Ordinal) && e.Name.Length > 0))
                {
                    var name = Path.GetFileName(entry.Name);
                    if (name.Length > 0 && name == entry.Name)
                    {
                        entry.ExtractToFile(Path.Combine(attachmentsStaging, name), overwrite: true);
                    }
                }
            }

            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                File.Delete(paths.DatabasePath + suffix);
            }

            File.Move(Path.Combine(staging, DatabaseEntry), paths.DatabasePath);
            if (Directory.Exists(paths.AttachmentsDirectory))
            {
                Directory.Delete(paths.AttachmentsDirectory, recursive: true);
            }

            Directory.Move(Path.Combine(staging, "attachments"), paths.AttachmentsDirectory);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }

        // The backup may come from an older version: bring its schema and search index up to date.
        await initializer.InitializeAsync(cancellationToken);
        logger.LogInformation("Backup {Path} restored", path);
        return safety;
    }

    private async Task<BackupManifest> DescribeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var migrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        return new BackupManifest(
            Format,
            FormatVersion,
            typeof(BackupService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0",
            migrations.LastOrDefault() ?? string.Empty,
            now,
            await context.Notebooks.CountAsync(cancellationToken),
            await context.Notes.CountAsync(cancellationToken),
            await context.Attachments.CountAsync(cancellationToken));
    }

    /// <summary>Opens the backed-up database by itself to be sure it is whole and not from a newer version.</summary>
    private async Task<List<string>> CheckDatabaseAsync(ZipArchiveEntry database, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var probe = Path.Combine(Path.GetTempPath(), $"worriorvex-probe-{Guid.NewGuid():N}.db");
        try
        {
            database.ExtractToFile(probe);
            await using var connection = new SqliteConnection($"Data Source={probe};Mode=ReadOnly");
            await connection.OpenAsync(cancellationToken);
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check";
                var result = (string?)await check.ExecuteScalarAsync(cancellationToken);
                if (result != "ok")
                {
                    problems.Add("The database in the backup is damaged: " + result);
                }
            }

            await using (var applied = connection.CreateCommand())
            {
                applied.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1";
                var latest = (string?)await applied.ExecuteScalarAsync(cancellationToken);
                await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
                var known = context.Database.GetMigrations().ToList();
                if (latest is not null && !known.Contains(latest))
                {
                    problems.Add($"The backup's database is from a newer WorriorVex (schema {latest}). Update WorriorVex to restore it.");
                }
            }
        }
        catch (SqliteException ex)
        {
            problems.Add("The database in the backup could not be opened: " + ex.Message);
        }
        finally
        {
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={probe};Mode=ReadOnly"));
            File.Delete(probe);
        }

        return problems;
    }
}
