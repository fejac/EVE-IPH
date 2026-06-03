using System.IO;
using System.IO.Compression;
using System.Net.Http;
using EveIndustryPlanner.Core;

namespace EveIndustryPlanner.App;

public static class SdeDownloadService
{
    private const string LatestYamlSdeUrl = "https://developers.eveonline.com/static-data/eve-online-static-data-latest-yaml.zip";

    private static readonly string[] RequiredFiles =
    [
        "blueprints.yaml",
        "types.yaml"
    ];

    public static string DefaultSdeDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "EveIndustryPlanner", "sde");
        }
    }

    public static async Task EnsureAvailableAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (SdeIndustryDataProvider.IsAvailable(DefaultSdeDirectory))
        {
            progress?.Report("Using cached SDE data");
            return;
        }

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EveIndustryPlanner");
        var downloadPath = Path.Combine(appData, "eve-online-static-data-latest-yaml.zip");
        var tempDirectory = Path.Combine(appData, $"sde-download-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(tempDirectory);

            progress?.Report("Downloading latest EVE SDE");
            using var httpClient = new HttpClient();
            await using (var downloadStream = await httpClient.GetStreamAsync(LatestYamlSdeUrl, cancellationToken))
            await using (var fileStream = File.Create(downloadPath))
            {
                await downloadStream.CopyToAsync(fileStream, cancellationToken);
            }

            progress?.Report("Extracting EVE SDE");
            ZipFile.ExtractToDirectory(downloadPath, tempDirectory);

            if (!ContainsRequiredFiles(tempDirectory))
            {
                progress?.Report("Downloaded SDE is missing required files");
                return;
            }

            Directory.CreateDirectory(DefaultSdeDirectory);
            foreach (var filePath in Directory.EnumerateFiles(tempDirectory, "*.yaml", SearchOption.AllDirectories))
            {
                var destinationPath = Path.Combine(DefaultSdeDirectory, Path.GetFileName(filePath));
                File.Copy(filePath, destinationPath, overwrite: true);
            }

            progress?.Report("Latest EVE SDE ready");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            progress?.Report($"Could not download SDE; sample data will be used: {ex.Message}");
        }
        finally
        {
            TryDeleteFile(downloadPath);
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static bool ContainsRequiredFiles(string directory)
    {
        var fileNames = Directory
            .EnumerateFiles(directory, "*.yaml", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return RequiredFiles.All(fileNames.Contains);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
