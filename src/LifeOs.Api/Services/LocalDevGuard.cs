using Npgsql;

namespace LifeOs.Api.Services;

public static class LocalDevGuard
{
    public static void Validate(string environment, string connectionString)
    {
        var db = new NpgsqlConnectionStringBuilder(connectionString);
        if (environment != "Development" || db.Host is not ("127.0.0.1" or "localhost") ||
            db.Port != 55439 || db.Database != "lifeos_dev" || db.Username != "lifeos_dev")
            throw new InvalidOperationException("Local Dev requires Development and its dedicated localhost:55439/lifeos_dev database and user.");
    }
}
