using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PdfEdit.Blazor.Services;

/// <summary>
/// Settings, custom stamps and fill-in profiles a browser keeps, as JSON in the same SQLite
/// database as saved signatures (pdfedit.db in the app's folder, or PdfEdit:Database). Each
/// browser has its own random ID; API keys, passwords and Digital IDs are never stored here.
/// </summary>
public sealed class UserPrefs
{
    private readonly string _connection;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public UserPrefs(IConfiguration config, IWebHostEnvironment env)
    {
        string path = config["PdfEdit:Database"] is { Length: > 0 } p ? p : Path.Combine(env.ContentRootPath, "pdfedit.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS prefs (
                owner TEXT NOT NULL,
                key TEXT NOT NULL,           -- settings | stamps | profiles
                json TEXT NOT NULL,
                updated TEXT NOT NULL,
                PRIMARY KEY (owner, key)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connection);
        db.Open();
        return db;
    }

    public T? Get<T>(string owner, string key) where T : class
    {
        if (!ValidOwner(owner)) return null;
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT json FROM prefs WHERE owner = $o AND key = $k";
        cmd.Parameters.AddWithValue("$o", owner);
        cmd.Parameters.AddWithValue("$k", key);
        try { return cmd.ExecuteScalar() is string s ? JsonSerializer.Deserialize<T>(s, Json) : null; }
        catch (JsonException) { return null; }
    }

    public void Set<T>(string owner, string key, T value)
    {
        if (!ValidOwner(owner)) return;
        var json = JsonSerializer.Serialize(value);
        if (json.Length > 1_000_000) throw new InvalidOperationException("That's too much to keep.");
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO prefs (owner, key, json, updated) VALUES ($o, $k, $j, $u)
            ON CONFLICT(owner, key) DO UPDATE SET json = excluded.json, updated = excluded.updated
            """;
        cmd.Parameters.AddWithValue("$o", owner);
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$j", json);
        cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static bool ValidOwner(string? owner) => Guid.TryParseExact(owner, "N", out _);
}

/// <summary>The web version's Settings (the parts of the Windows app's that apply in a browser).</summary>
public sealed class WebSettings
{
    /// <summary>Name on dynamic stamps and comments.</summary>
    public string AuthorName { get; set; } = "";
    /// <summary>Date format for the Date tool (a .NET pattern).</summary>
    public string DateFormat { get; set; } = "d MMMM yyyy";
    public int DefaultFontSize { get; set; } = 12;
    public string DefaultTextColour { get; set; } = "#000000";
    /// <summary>Start each document at Fit Width instead of 100%.</summary>
    public bool OpenFitWidth { get; set; }
    public bool HighlightFields { get; set; } = true;
    public double UiScale { get; set; } = 1;
    public bool ShowTipsAtStart { get; set; }
}
