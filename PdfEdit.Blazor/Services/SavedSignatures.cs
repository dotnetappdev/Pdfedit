using Microsoft.Data.Sqlite;

namespace PdfEdit.Blazor.Services;

/// <summary>A saved signature or initials image.</summary>
public sealed record StoredSignature(string Id, string Kind, byte[] Png, DateTime Created)
{
    public string DataUrl => "data:image/png;base64," + Convert.ToBase64String(Png);
}

/// <summary>
/// Signatures and initials people chose to keep, in a SQLite database in the app's folder
/// (pdfedit.db, or PdfEdit:Database). The web version has no accounts, so each browser has its own
/// random ID and only sees what it saved; anyone using the same browser shares them.
/// </summary>
public sealed class SavedSignatures
{
    private const int MaxPerKind = 8;
    private readonly string _connection;

    public SavedSignatures(IConfiguration config, IWebHostEnvironment env)
    {
        string path = config["PdfEdit:Database"] is { Length: > 0 } p ? p : Path.Combine(env.ContentRootPath, "pdfedit.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS signatures (
                id TEXT PRIMARY KEY,
                owner TEXT NOT NULL,
                kind TEXT NOT NULL,          -- signature | initials
                png BLOB NOT NULL,
                created TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS signatures_owner ON signatures(owner, kind, created);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connection);
        db.Open();
        return db;
    }

    /// <summary>A browser's saved signatures and initials, newest first.</summary>
    public List<StoredSignature> List(string owner)
    {
        var list = new List<StoredSignature>();
        if (!ValidOwner(owner)) return list;
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, kind, png, created FROM signatures WHERE owner = $o ORDER BY created DESC";
        cmd.Parameters.AddWithValue("$o", owner);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StoredSignature(r.GetString(0), r.GetString(1), (byte[])r["png"], DateTime.Parse(r.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        return list;
    }

    /// <summary>Keeps a signature or initials (the oldest go once there are more than eight of a kind).</summary>
    public StoredSignature? Add(string owner, string kind, byte[] png)
    {
        if (!ValidOwner(owner) || kind is not ("signature" or "initials") || png.Length is 0 or > 2_000_000) return null;
        var item = new StoredSignature(Guid.NewGuid().ToString("N"), kind, png, DateTime.UtcNow);
        using var db = Open();
        using var tx = db.BeginTransaction();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO signatures (id, owner, kind, png, created) VALUES ($id, $o, $k, $png, $c)";
            cmd.Parameters.AddWithValue("$id", item.Id);
            cmd.Parameters.AddWithValue("$o", owner);
            cmd.Parameters.AddWithValue("$k", kind);
            cmd.Parameters.AddWithValue("$png", png);
            cmd.Parameters.AddWithValue("$c", item.Created.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                DELETE FROM signatures WHERE owner = $o AND kind = $k AND id NOT IN
                    (SELECT id FROM signatures WHERE owner = $o AND kind = $k ORDER BY created DESC LIMIT $max)
                """;
            cmd.Parameters.AddWithValue("$o", owner);
            cmd.Parameters.AddWithValue("$k", kind);
            cmd.Parameters.AddWithValue("$max", MaxPerKind);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return item;
    }

    public void Delete(string owner, string id)
    {
        if (!ValidOwner(owner)) return;
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM signatures WHERE owner = $o AND id = $id";
        cmd.Parameters.AddWithValue("$o", owner);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // The browser's ID is a random GUID it made; anything else is ignored.
    private static bool ValidOwner(string? owner) => Guid.TryParseExact(owner, "N", out _);
}
