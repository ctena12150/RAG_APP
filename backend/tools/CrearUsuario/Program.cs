using System.Text;
using Npgsql;

namespace RAG.Tools.CrearUsuario;

/// <summary>
/// Utilidad de consola para crear o actualizar un usuario local de acceso al chat
/// (tabla app.usuarios, esquema "app"). La contraseña se guarda siempre como hash BCrypt.
///
/// Uso:
///   dotnet run --project backend/tools/CrearUsuario -- \
///       --connection "Host=...;Port=5432;Database=ragapp;Username=...;Password=..." \
///       --usuario alice --email alice@empresa.com --nombre "Alice Ruiz" [--password "..."]
///
/// Si no se pasa --password, se solicita de forma interactiva sin eco. El connection string
/// también puede venir en la variable de entorno RAG_POSTGRES.
/// </summary>
public static class Program
{
    private const string SqlCrearTabla = """
        CREATE TABLE IF NOT EXISTS app.usuarios (
            id            uuid PRIMARY KEY,
            usuario       varchar(100) NOT NULL,
            password_hash varchar(255) NOT NULL,
            email         varchar(320),
            nombre        varchar(200),
            activo        boolean      NOT NULL DEFAULT true,
            creado_utc    timestamptz  NOT NULL DEFAULT now()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_usuarios_usuario ON app.usuarios(lower(usuario));
        """;

    private const string UpsertSql = """
        INSERT INTO app.usuarios (id, usuario, password_hash, email, nombre, activo, creado_utc)
        VALUES (@id, @usuario, @password_hash, @email, @nombre, true, now())
        ON CONFLICT ((lower(usuario))) DO UPDATE SET
            password_hash = EXCLUDED.password_hash,
            email = EXCLUDED.email,
            nombre = EXCLUDED.nombre,
            activo = true
        """;

    public static async Task<int> Main(string[] args)
    {
        var (conexion, usuario, email, nombre, password) = ParsearArgs(args);
        if (string.IsNullOrWhiteSpace(conexion))
        {
            Console.Error.WriteLine("Falta el connection string: usa --connection \"...\" o la variable de entorno RAG_POSTGRES.");
            return 2;
        }
        if (string.IsNullOrWhiteSpace(usuario))
        {
            Console.Error.WriteLine("Falta --usuario (nombre de usuario del login local).");
            return 2;
        }
        if (password is null)
        {
            password = LeerPassword($"Contraseña para '{usuario}'");
        }
        if (password.Length < 8)
        {
            Console.Error.WriteLine("La contraseña debe tener al menos 8 caracteres.");
            return 2;
        }

        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        Console.WriteLine($"Generado hash BCrypt ({hash.Length} caracteres).");

        var cadena = NormalizarTimeout(conexion);
        try
        {
            await using var conn = new NpgsqlConnection(cadena);
            await conn.OpenAsync();

            await using (var ddl = new NpgsqlCommand(SqlCrearTabla, conn))
                await ddl.ExecuteNonQueryAsync();

            await using var cmd = new NpgsqlCommand(UpsertSql, conn);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("usuario", usuario);
            cmd.Parameters.AddWithValue("password_hash", hash);
            cmd.Parameters.AddWithValue("email", ODb(email));
            cmd.Parameters.AddWithValue("nombre", ODb(nombre));
            await cmd.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (ex.SqlState == "42P01")
        {
            Console.Error.WriteLine("Falta la tabla 'app.usuarios' y no se pudo crearla automáticamente. Aplica scripts/postgres/schema.sql en la base de datos.");
            return 1;
        }
        catch (NpgsqlException ex)
        {
            Console.Error.WriteLine($"No se pudo escribir en Postgres: {MensajeConexion(ex)}. Revisa host/puerto/firewall y la variable RAG_POSTGRES.");
            return 1;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Tiempo de espera agotado al conectar con Postgres. Revisa host/puerto/firewall (¿túnel SSH activo?).");
            return 1;
        }

        Console.WriteLine($"Usuario '{usuario}' creado/actualizado en app.usuarios.");
        return 0;
    }

    private static (string? Conexion, string Usuario, string? Email, string? Nombre, string? Password) ParsearArgs(string[] args)
    {
        string? conexion = Environment.GetEnvironmentVariable("RAG_POSTGRES");
        string? usuario = null;
        string? email = null;
        string? nombre = null;
        string? password = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--connection": conexion = ValorSiguiente(args, ref i); break;
                case "--usuario": usuario = ValorSiguiente(args, ref i); break;
                case "--email": email = ValorSiguiente(args, ref i); break;
                case "--nombre": nombre = ValorSiguiente(args, ref i); break;
                case "--password": password = ValorSiguiente(args, ref i); break;
                case "--help":
                case "-h":
                    MostrarAyuda();
                    Environment.Exit(0);
                    break;
            }
        }

        return (conexion, usuario ?? string.Empty, email, nombre, password);
    }

    private static string ValorSiguiente(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine($"Falta el valor del argumento '{args[i]}'.");
            Environment.Exit(2);
        }
        return args[++i];
    }

    private static string LeerPassword(string etiqueta)
    {
        Console.Write($"{etiqueta}: ");
        var sb = new StringBuilder();
        while (true)
        {
            var tecla = Console.ReadKey(intercept: true);
            if (tecla.Key == ConsoleKey.Enter) break;
            if (tecla.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Length--;
                continue;
            }
            if (!char.IsControl(tecla.KeyChar)) sb.Append(tecla.KeyChar);
        }
        Console.WriteLine();
        return sb.ToString();
    }

    private static string? Normalizar(string? valor)
    {
        var limpio = valor?.Trim();
        return string.IsNullOrWhiteSpace(limpio) ? null : limpio;
    }

    private static object ODb(string? valor) => Normalizar(valor) is { } limpio ? limpio : DBNull.Value;

    private static string NormalizarTimeout(string conexion)
    {
        try
        {
            var csb = new NpgsqlConnectionStringBuilder(conexion);
            if (csb.Timeout <= 0 || csb.Timeout > 30) csb.Timeout = 15;
            if (csb.CommandTimeout <= 0 || csb.CommandTimeout > 60) csb.CommandTimeout = 15;
            return csb.ToString();
        }
        catch
        {
            return conexion;
        }
    }

    private static string MensajeConexion(NpgsqlException ex)
    {
        var texto = ex.Message.Trim();
        if (texto.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase))
            return "no hay ruta TCP al servidor (puerto cerrado o sin túnel SSH)";
        if (!string.IsNullOrWhiteSpace(ex.SqlState))
            return $"error SQL {ex.SqlState}: {texto}";
        return texto;
    }

    private static void MostrarAyuda()
    {
        Console.WriteLine("""
            Uso: dotnet run --project backend/tools/CrearUsuario -- [opciones]

              --connection "Host=...;Port=5432;Database=ragapp;Username=...;Password=..."
                    Connection string de Postgres. Alternativa: variable RAG_POSTGRES.
              --usuario    <nombre>  Nombre de usuario del login local (obligatorio).
              --email      <email>   Email opcional asociado al usuario (claim de sesión).
              --nombre     <nombre>  Nombre visible opcional.
              --password   <clave>   Contraseña. Si se omite, se pide sin eco.
            """);
    }
}