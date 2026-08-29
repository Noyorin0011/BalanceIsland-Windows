using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalanceIsland.Windows;

public sealed class AppDataStore
{
    private readonly string _directory;
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppDataStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BalanceIsland"))
    {
    }

    public AppDataStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _path = Path.Combine(_directory, "state.json");
    }

    public AppDataLoadResult LoadResult()
    {
        try
        {
            if (!File.Exists(_path))
                return new AppDataLoadResult(AppStateNormalizer.Normalize(new AppState()), false, null);

            var json = File.ReadAllText(_path);
            AppStateSemanticValidator.ValidateJsonTokens(json);
            var state = JsonSerializer.Deserialize<AppState>(json, _json)
                ?? throw new JsonException("状态文件不包含应用状态。");
            AppStateSemanticValidator.Validate(state);
            return new AppDataLoadResult(AppStateNormalizer.Normalize(state), true, null);
        }
        catch (Exception exception)
        {
            return new AppDataLoadResult(
                AppStateNormalizer.Normalize(new AppState()),
                false,
                exception.Message);
        }
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(_directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, _json));
        File.Move(temporary, _path, overwrite: true);
    }
}

public sealed record AppDataLoadResult(AppState State, bool LoadedFromDisk, string? Error);
