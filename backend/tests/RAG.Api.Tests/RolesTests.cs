using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RAG.Api.Tests.Infrastructure;
using RAG.Domain.Models;
using RAG.Infrastructure.Stores.InMemory;

namespace RAG.Api.Tests;

/// <summary>
/// Autorización por rol y propiedad de conversaciones: el usuario solo consulta, el teamleader
/// sube/gestiona documentos y el superusuario administra usuarios locales.
/// </summary>
public sealed class RolesTests :
    IClassFixture<AuthApiTestFactory>,
    IClassFixture<AuthTeamLeaderApiTestFactory>,
    IClassFixture<AuthSuperUsuarioApiTestFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly AuthApiTestFactory _usuario;
    private readonly AuthTeamLeaderApiTestFactory _teamleader;
    private readonly AuthSuperUsuarioApiTestFactory _superusuario;

    public RolesTests(
        AuthApiTestFactory usuario,
        AuthTeamLeaderApiTestFactory teamleader,
        AuthSuperUsuarioApiTestFactory superusuario)
    {
        _usuario = usuario;
        _teamleader = teamleader;
        _superusuario = superusuario;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string fileName, string dominio, string content)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new StringContent(content, Encoding.UTF8, "text/plain");
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(dominio), "dominio");
        return await client.PostAsync("/api/documents/upload", form);
    }

    // --- usuario: solo consulta ---

    [Fact]
    public async Task Usuario_no_puede_subir_documentos_devuelve_403()
    {
        var client = _usuario.CreateClient();
        using var form = new MultipartFormDataContent();
        var response = await client.PostAsync("/api/documents/upload", form);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_no_puede_borrar_documentos_devuelve_403()
    {
        var client = _usuario.CreateClient();
        var response = await client.DeleteAsync($"/api/documents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_no_puede_crear_carpetas_devuelve_403()
    {
        var client = _usuario.CreateClient();
        var response = await client.PostAsJsonAsync("/api/folders", new { nombre = "Carpeta", dominio = "rrhh" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_sin_sesion_no_accede_a_administracion()
    {
        using var factory = new AuthApiTestFactorySinSesion();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/usuarios");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_realiza_flujo_completo_de_chat_con_su_sesion()
    {
        var client = _usuario.CreateClient();
        var created = await (await client.PostAsJsonAsync("/api/conversations", new { dominios = new[] { "rrhh" } }))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = created.GetProperty("id").GetString()!;

        var list = await client.GetFromJsonAsync<JsonElement>("/api/conversations", Json);
        Assert.Contains(list.EnumerateArray(), c => c.GetProperty("id").GetString() == id);
    }

    // --- teamleader: sube y gestiona documentos ---

    [Fact]
    public async Task Teamleader_puede_subir_documento()
    {
        var client = _teamleader.CreateClient();
        var response = await UploadAsync(client, "politica-vacaciones.txt", "rrhh",
            "Política de vacaciones: 23 días naturales por año.");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Teamleader_puede_crear_y_borrar_carpeta()
    {
        var client = _teamleader.CreateClient();
        var created = await (await client.PostAsJsonAsync("/api/folders", new { nombre = "Documentación", dominio = "mantenimiento" }))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = created.GetProperty("id").GetString()!;
        var deleteResponse = await client.DeleteAsync($"/api/folders/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Teamleader_no_accede_a_administracion_de_usuarios()
    {
        var client = _teamleader.CreateClient();
        var response = await client.GetAsync("/api/usuarios");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- superusuario: administración de usuarios locales ---

    [Fact]
    public async Task Superusuario_lista_usuarios_locales()
    {
        var client = _superusuario.CreateClient();
        var usuarios = await client.GetFromJsonAsync<JsonElement>("/api/usuarios", Json);
        var nombres = usuarios.EnumerateArray().Select(u => u.GetProperty("usuario").GetString()).ToList();
        Assert.Contains("carla", nombres);
    }

    [Fact]
    public async Task Superusuario_crea_usuario_y_lo_desactiva()
    {
        var client = _superusuario.CreateClient();
        var response = await client.PostAsJsonAsync("/api/usuarios", new
        {
            usuario = "nuevo",
            contrasena = "ClaveTest123!",
            rol = "teamleader",
            nombre = "Nuevo Usuario"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = created.GetProperty("id").GetString()!;

        // login local con la cuenta recién creada funciona (misma fábrica: mismo store InMemory)
        var login = await client.PostAsJsonAsync("/auth/login", new { usuario = "nuevo", contrasena = "ClaveTest123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("teamleader", loginBody.GetProperty("rol").GetString());

        var patch = await client.PatchAsJsonAsync($"/api/usuarios/{id}", new { activo = false });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        // desactivado → el login local vuelve a dar 401
        var loginTrasBaja = await client.PostAsJsonAsync("/auth/login", new { usuario = "nuevo", contrasena = "ClaveTest123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginTrasBaja.StatusCode);
    }

    [Fact]
    public async Task Superusuario_crear_usuario_duplicado_devuelve_409()
    {
        var client = _superusuario.CreateClient();
        var body = new { usuario = "alice", contrasena = "ClaveTest123!", rol = "usuario" };
        await client.PostAsJsonAsync("/api/usuarios", body);
        var segunda = await client.PostAsJsonAsync("/api/usuarios", body);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        var error = await segunda.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("usuario_duplicado", error.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Superusuario_crear_usuario_con_rol_invalido_devuelve_400()
    {
        var client = _superusuario.CreateClient();
        var response = await client.PostAsJsonAsync("/api/usuarios", new
        {
            usuario = "raro",
            contrasena = "ClaveTest123!",
            rol = "administrador"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Superusuario_cambia_el_rol_de_un_usuario()
    {
        var client = _superusuario.CreateClient();
        var created = await (await client.PostAsJsonAsync("/api/usuarios", new
        {
            usuario = "mario",
            contrasena = "ClaveTest123!",
            rol = "usuario"
        })).Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = created.GetProperty("id").GetString()!;

        var updated = await client.PatchAsJsonAsync($"/api/usuarios/{id}", new { rol = "superusuario" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = (await updated.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("rol").GetString();
        Assert.Equal("superusuario", body);
    }
}

/// <summary>
/// Gestión de usuarios por el superusuario y acotación de dominios del teamleader:
/// edición completa, borrado con protecciones y 403 fuera de sus dominios.
/// </summary>
public sealed class UsuariosGestionTests(
    AuthSuperUsuarioApiTestFactory superusuario,
    AuthTeamLeaderRrhhApiTestFactory acotado)
    : IClassFixture<AuthSuperUsuarioApiTestFactory>, IClassFixture<AuthTeamLeaderRrhhApiTestFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string fileName, string dominio, string content)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new StringContent(content, Encoding.UTF8, "text/plain");
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(dominio), "dominio");
        return await client.PostAsync("/api/documents/upload", form);
    }

    private async Task<string> CrearUsuarioAsync(object body)
    {
        var client = superusuario.CreateClient();
        var response = await client.PostAsJsonAsync("/api/usuarios", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Teamleader_acotado_sube_a_su_dominio_pero_no_a_otro()
    {
        var client = acotado.CreateClient();
        var permitido = await UploadAsync(client, "guia-rrhh-acotado.txt", "rrhh",
            "Guía de RRHH para el test de dominios acotados del teamleader.");
        Assert.Equal(HttpStatusCode.Created, permitido.StatusCode);

        var denegado = await UploadAsync(client, "guia-mant-acotado.txt", "mantenimiento",
            "Guía de mantenimiento para el test de dominios acotados del teamleader.");
        Assert.Equal(HttpStatusCode.Forbidden, denegado.StatusCode);
        var error = await denegado.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("dominio_no_permitido", error.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Teamleader_acotado_crea_carpeta_solo_en_su_dominio()
    {
        var client = acotado.CreateClient();
        var permitida = await client.PostAsJsonAsync("/api/folders", new { nombre = "RRHH-Acotado", dominio = "rrhh" });
        Assert.Equal(HttpStatusCode.Created, permitida.StatusCode);

        var denegada = await client.PostAsJsonAsync("/api/folders", new { nombre = "MANT-Acotado", dominio = "mantenimiento" });
        Assert.Equal(HttpStatusCode.Forbidden, denegada.StatusCode);
    }

    [Fact]
    public async Task Superusuario_edita_dominio_invalido_devuelve_400()
    {
        var client = superusuario.CreateClient();
        var id = await CrearUsuarioAsync(new { usuario = "domerr", contrasena = "ClaveTest123!", rol = "teamleader" });
        var response = await client.PatchAsJsonAsync($"/api/usuarios/{id}", new { dominios = new[] { "inexistente" } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("dominio_invalido", error.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Superusuario_actualiza_datos_contrasena_y_dominios()
    {
        var client = superusuario.CreateClient();
        var id = await CrearUsuarioAsync(new
        {
            usuario = "editado1",
            contrasena = "ClaveTest123!",
            rol = "teamleader",
            dominios = new[] { "rrhh" }
        });

        var patch = await client.PatchAsJsonAsync($"/api/usuarios/{id}", new
        {
            nombre = "Editado Uno",
            email = "editado1@empresa.com",
            contrasena = "NuevaClave456!",
            dominios = new[] { "rrhh", "onboarding" }
        });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var body = await patch.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Editado Uno", body.GetProperty("nombre").GetString());
        Assert.Equal(2, body.GetProperty("dominios").GetArrayLength());

        // la contraseña vieja ya no vale; la nueva sí
        var vieja = await client.PostAsJsonAsync("/auth/login", new { usuario = "editado1", contrasena = "ClaveTest123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, vieja.StatusCode);
        var nueva = await client.PostAsJsonAsync("/auth/login", new { usuario = "editado1", contrasena = "NuevaClave456!" });
        Assert.Equal(HttpStatusCode.OK, nueva.StatusCode);
    }

    [Fact]
    public async Task Superusuario_borra_usuario_y_pierde_acceso()
    {
        var client = superusuario.CreateClient();
        var id = await CrearUsuarioAsync(new { usuario = "borrado1", contrasena = "ClaveTest123!", rol = "usuario" });

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/usuarios/{id}")).StatusCode);

        var lista = await client.GetFromJsonAsync<JsonElement>("/api/usuarios", Json);
        Assert.DoesNotContain(lista.EnumerateArray(), u => u.GetProperty("usuario").GetString() == "borrado1");

        var login = await client.PostAsJsonAsync("/auth/login", new { usuario = "borrado1", contrasena = "ClaveTest123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Borrar_propio_usuario_devuelve_409()
    {
        // el email coincide con la identidad TestAuth del superusuario: es "uno mismo"
        var client = superusuario.CreateClient();
        var id = await CrearUsuarioAsync(new { usuario = "jefazo", contrasena = "ClaveTest123!", rol = "superusuario", email = "admin@empresa.com" });

        var response = await client.DeleteAsync($"/api/usuarios/{id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("usuario_propio", error.GetProperty("error").GetProperty("code").GetString());

        // limpieza: degradado a usuario para no interferir con otros tests (orden no garantizado)
        var degradar = await client.PatchAsJsonAsync($"/api/usuarios/{id}", new { rol = "usuario" });
        Assert.Equal(HttpStatusCode.OK, degradar.StatusCode);
    }

    [Fact]
    public async Task Borrar_ultimo_superusuario_devuelve_409()
    {
        var client = superusuario.CreateClient();
        var verdugo = await CrearUsuarioAsync(new { usuario = "verdugo", contrasena = "ClaveTest123!", rol = "superusuario" });
        var victima = await CrearUsuarioAsync(new { usuario = "victima", contrasena = "ClaveTest123!", rol = "superusuario" });

        // el verdugo pierde el rol: la víctima queda como último superusuario activo
        var degradar = await client.PatchAsJsonAsync($"/api/usuarios/{verdugo}", new { rol = "usuario" });
        Assert.Equal(HttpStatusCode.OK, degradar.StatusCode);

        var response = await client.DeleteAsync($"/api/usuarios/{victima}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("ultimo_superusuario", error.GetProperty("error").GetProperty("code").GetString());
    }
}

/// <summary>Filtrado por dueño del almacén de conversaciones (historial privado).</summary>
public sealed class ConversacionOwnershipStoreTests
{
    [Fact]
    public async Task Listar_filtra_por_dueño_e_ignora_las_ajenas()
    {
        var store = new InMemoryConversationStore();
        await store.CreateAsync(Conversacion("c1", "a@empresa.com"));
        await store.CreateAsync(Conversacion("c2", "b@empresa.com"));
        await store.CreateAsync(Conversacion("c3", "A@EMPRESA.COM"));

        var deA = await store.ListAsync(usuarioId: "a@empresa.com");
        Assert.Equal(2, deA.Count);
        Assert.All(deA, c => Assert.Equal("a@empresa.com", c.UsuarioId!.ToLowerInvariant()));

        var deB = await store.ListAsync(usuarioId: "b@empresa.com");
        Assert.Single(deB);
    }

    [Fact]
    public async Task Listar_sin_dueño_devuelve_historico_compartido()
    {
        var store = new InMemoryConversationStore();
        await store.CreateAsync(Conversacion("c1", "a@empresa.com"));
        await store.CreateAsync(Conversacion("c2", null));

        var todas = await store.ListAsync();
        Assert.Equal(2, todas.Count);
    }

    private static Conversation Conversacion(string id, string? usuarioId) => new()
    {
        Id = Guid.Parse($"00000000-0000-0000-0000-{id.PadLeft(12, '0')}"),
        Titulo = $"Título {id}",
        TituloAutomatico = true,
        UsuarioId = usuarioId,
        CreadoUtc = DateTime.UtcNow,
        ActualizadoUtc = DateTime.UtcNow
    };
}