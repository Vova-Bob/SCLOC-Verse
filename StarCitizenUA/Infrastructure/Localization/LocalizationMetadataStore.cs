using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StarCitizenUA.Domain.Localization;

namespace StarCitizenUA.Infrastructure.Localization;

/// <summary>
/// Робота з файлом метаданих локалізації у %LOCALAPPDATA%.
/// </summary>
internal sealed class LocalizationMetadataStore
{
    private const string CacheRootFolderName = "StarCitizenUA";
    private const string CacheSubFolderName = "cache";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Зчитує метадані для середовища або повертає null, якщо файл відсутній чи пошкоджений.
    /// </summary>
    public async Task<LocalizationMetadata?> ReadAsync(string environmentName, CancellationToken ct)
    {
        var path = GetMetadataPath(environmentName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(path, new FileStreamOptions
            {
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Mode = FileMode.Open,
                Options = FileOptions.Asynchronous
            });

            return await JsonSerializer.DeserializeAsync<LocalizationMetadata>(stream, SerializerOptions, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Записує метадані у файл, створюючи директорію за потреби.
    /// </summary>
    public async Task WriteAsync(string environmentName, LocalizationMetadata metadata, CancellationToken ct)
    {
        var path = GetMetadataPath(environmentName);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Access = FileAccess.Write,
            Share = FileShare.None,
            Mode = FileMode.Create,
            Options = FileOptions.Asynchronous
        });

        await JsonSerializer.SerializeAsync(stream, metadata, SerializerOptions, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Повертає шлях до файлу метаданих для середовища.
    /// </summary>
    public string GetMetadataPath(string environmentName)
    {
        var sanitizedName = Sanitize(environmentName);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, CacheRootFolderName, CacheSubFolderName, $"{sanitizedName}.meta.json");
    }

    private static string Sanitize(string environmentName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeChars = environmentName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        var candidate = new string(safeChars).Trim();
        return string.IsNullOrWhiteSpace(candidate) ? "UNKNOWN" : candidate;
    }
}
