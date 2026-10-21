using Npgsql;

namespace MeuBar;

public static class DatabaseSettings
{
    public static string ConnectionString(IConfiguration config)
    {
        var configured = config.GetConnectionString("Database");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var url = config["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Configure ConnectionStrings__Database ou DATABASE_URL.");

        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("postgres" or "postgresql") ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.Fragment))
                throw new FormatException();

            var credentials = uri.UserInfo.Split(':', 2);
            var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            if (credentials.Length != 2 || string.IsNullOrEmpty(credentials[0]) || string.IsNullOrEmpty(database))
                throw new FormatException();

            var connection = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port < 0 ? 5432 : uri.Port,
                Username = Uri.UnescapeDataString(credentials[0]),
                Password = Uri.UnescapeDataString(credentials[1]),
                Database = database
            };
            foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = parameter.Split('=', 2);
                if (parts.Length != 2) throw new FormatException();
                var name = Uri.UnescapeDataString(parts[0]);
                var value = Uri.UnescapeDataString(parts[1]);
                if (name == "sslmode" && Enum.TryParse<SslMode>(value.Replace("-", ""), true, out var sslMode))
                    connection.SslMode = sslMode;
                else if (name == "application_name") connection.ApplicationName = value;
                else throw new FormatException();
            }
            return connection.ConnectionString;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // URLs podem conter credenciais: não as inclua em mensagens nem inner exceptions.
            throw new InvalidOperationException("DATABASE_URL inválida. Use uma URL PostgreSQL com usuário, senha e banco; opções aceitas: sslmode e application_name.");
        }
    }
}
