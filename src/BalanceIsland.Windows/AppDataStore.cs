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
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BalanceIsland");
        _path = Path.Combine(_directory, "state.json");
    }

    public AppState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppState();
            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(_path), _json)
                ?? new AppState();
        }
        catch
        {
            return new AppState();
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
