using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RAG.Api.Tests.Infrastructure;
using RAG.Infrastructure.Stores.InMemory;
using RAG.Domain.Models;

namespace RAG.Api.Tests;

/// <summary>
/// Puerta de acceso al chat: con Auth habilitada, /api/conversations exige sesión (401 sin
/// ella) y /auth/me describe el estado de la sesión. El resto de /api queda abierto (alcance
/// decidido: solo la entrada al chat).
/// </summary>
public sealed class AuthSinSesionTests(AuthApiTestFactorySinSesion factory) : IClassFixture<AuthApiTestFactorySinSesion>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Conversaciones_sin_sesion_devuelven_401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/conversations");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_sin_sesion_devuelve_autenticado_false()
    {
        var client = factory.CreateClient();
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me", Json);
        Assert.False(me.GetProperty("autenticado").GetBoolean());
        Assert.Null(me.GetProperty("email").GetString());
        var proveedores = me.GetProperty("proveedores").EnumerateArray().Select(p => p.GetString()).ToHashSet();
        Assert.Contains("google", proveedores);
        Assert.Contains("local", proveedores);
        Assert.DoesNotContain("microsoft", proveedores);
    }

    [Fact]
    public async Task Documentos_siguen_abiertos_con_auth_habilitada()
    {
        // alcance elegido: la lista blanca protege el chat, no la ingesta/consulta
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Denegado_devuelve_403_controlado()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/auth/denegado");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("acceso_denegado", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_local_exitoso_devuelve_200_y_cookie_de_sesion()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new { usuario = "Carla", contrasena = "ClaveTest123!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(body.GetProperty("autenticado").GetBoolean());
        Assert.Equal("local", body.GetProperty("proveedor").GetString());
        Assert.Equal("carla@empresa.com", body.GetProperty("email").GetString());
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.Contains("rag.session")),
            "El login local debe emitir la cookie de sesión");
    }

    [Fact]
    public async Task Login_local_con_contraseña_incorrecta_devuelve_401()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new { usuario = "carla", contrasena = "clave-equivocada" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("credenciales_invalidas", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_local_con_usuario_inexistente_devuelve_401()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new { usuario = "nadie", contrasena = "ClaveTest123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("credenciales_invalidas", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_local_sin_campos_devuelve_401()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new { usuario = "", contrasena = "" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>Con sesión válida (TestAuth autenticado) el chat funciona.</summary>
public sealed class AuthConSesionTests(AuthApiTestFactory factory) : IClassFixture<AuthApiTestFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Me_con_sesion_devuelve_email_y_proveedor()
    {
        var client = factory.CreateClient();
        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me", Json);
        Assert.True(me.GetProperty("autenticado").GetBoolean());
        Assert.Equal("carla@empresa.com", me.GetProperty("email").GetString());
        Assert.Equal("google", me.GetProperty("proveedor").GetString());
    }

    [Fact]
    public async Task Conversaciones_con_sesion_responden_200()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/conversations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Crear_conversacion_con_sesion_funciona()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/conversations", new { dominios = new[] { "rrhh" } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("rrhh", body.GetProperty("dominios")[0].GetString());
    }
}

/// <summary>Matcher puro de la lista blanca (email exacto y dominio), sin host HTTP.</summary>
public sealed class UsuarioPermitidoStoreTests
{
    [Fact]
    public async Task Email_exacto_esta_permitido_y_el_resto_no()
    {
        var store = new InMemoryUsuarioPermitidoStore(
            [new UsuarioPermitido { Id = Guid.NewGuid(), Email = "carla@empresa.com", Activo = true }]);

        Assert.True(await store.EstaPermitidoAsync("Carla@Empresa.COM"));
        Assert.False(await store.EstaPermitidoAsync("otro@empresa.com"));
        Assert.False(await store.EstaPermitidoAsync("carla@otro.com"));
    }

    [Fact]
    public async Task Dominio_permitido_abarca_todo_el_dominio()
    {
        var store = new InMemoryUsuarioPermitidoStore(
            [new UsuarioPermitido { Id = Guid.NewGuid(), Dominio = "empresa.com", Activo = true }]);

        Assert.True(await store.EstaPermitidoAsync("quiensea@empresa.com"));
        Assert.False(await store.EstaPermitidoAsync("quiensea@empresa.es"));
    }

    [Fact]
    public async Task Entrada_inactiva_no_cuenta()
    {
        var store = new InMemoryUsuarioPermitidoStore(
            [new UsuarioPermitido { Id = Guid.NewGuid(), Email = "carla@empresa.com", Activo = false }]);

        Assert.False(await store.EstaPermitidoAsync("carla@empresa.com"));
    }
}