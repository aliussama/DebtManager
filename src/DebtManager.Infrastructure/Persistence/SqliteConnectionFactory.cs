using Microsoft.Data.Sqlite;
using DebtManager.Infrastructure.Security;

namespace DebtManager.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string _path;
    private readonly IKeyStore _keys;

    public SqliteConnectionFactory(string path, IKeyStore keys)
    {
        _path = path;
        _keys = keys;
    }

    public SqliteConnection Create()
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());

        conn.Open();

        var key = _keys.GetOrCreateKey();
        var hex = Convert.ToHexString(key);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA key = \"x'{hex}'\";";
            cmd.ExecuteNonQuery();
        }

        using (var verify = conn.CreateCommand())
        {
            verify.CommandText = "SELECT count(*) FROM sqlite_master;";
            verify.ExecuteScalar();
        }

        return conn;
    }
}
