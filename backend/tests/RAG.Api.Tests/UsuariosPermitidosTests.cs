using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RAG.Api.Tests.Infrastructure;

namespace RAG.Api.Tests;

/// <summary>
/// Administración de la lista blanca de acceso Google (solo superusuario):
/// alta de emails/dominios, duplicados, validación, activación y borrado.
/// </summary>
public sealed class UsuariosPermitidosTests(
    AuthSuperUsuarioApiTestFactory superusuario,
    AuthTeamLeaderApiTestFactory teamleader,
    AuthApiTestFactorySinSesion sinSesion)
    : IClassFixture<AuthSuperUsuarioApiTestFactory>,
      IClassFixture<AuthTeamLeaderApiTestFactory>,
      IClassFixture<AuthApiTestFactorySinSesion>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Superusuario_crea_email_y_dominio_y_lista()
    {
        var client = superusuario.CreateClient();
        var email = await client.PostAsJsonAsync("/api/usuarios-permitidos", new { email = "jefa@empresa.com" });
        Assert.Equal(HttpStatusCode.Created, email.StatusCode);
        var dominio = await client.PostAsJsonAsync("/api/usuarios-permitidos", new { dominio = "socios.com" });
        Assert.Equal(HttpStatusCode.Created, dominio.StatusCode);

        var lista = await client.GetFromJsonAsync<JsonElement>("/api/usuarios-permitidos", Json);
        var valores = lista.EnumerateArray()
            .Select(e => e.GetProperty("email").GetString() ?? e.GetProperty("dominio").GetString())
            .ToHashSet();
        Assert.Contains("jefa@empresa.com", valores);
        Assert.Contains("socios.com", valores);
    }

    [Fact]
    public async Task Crear_duplicado_devuelve_409()
    {
        var client = superusuario.CreateClient();
        var primero = await client.PostAsJsonAsync("/api/usuarios-permitidos", new { email = "repe@empresa.com" });
        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);

        var segundo = await client.PostAsJsonAsync("/api/usuarios-permitidos", new { email = "REPE@empresa.com" });
        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        var error = await segundo.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("entrada_duplicada", error.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Crear_sin_valor_o_con_ambos_devuelve_400()
    {
        var client = superusuario.CreateClient();
        foreach (var body in new object[]
        {
            new { email = "a@b.com", dominio = "b.com" },
            new { email = "sin-arroba" },
            new { dominio = "con@arroba.com" },
            new { },
        })
        {
            var response = await client.PostAsJsonAsync("/api/usuarios-permitidos", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Superusuario_desactiva_y_borra_entrada()
    {
        var client = superusuario.CreateClient();
        var created = await (await client.PostAsJsonAsync("/api/usuarios-permitidos", new { email = "saliente@empresa.com" }))
            .Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = created.GetProperty("id").GetString()!;

        var patch = await client.PatchAsJsonAsync($"/api/usuarios-permitidos/{id}", new { activo = false });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.False((await patch.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("activo").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/usuarios-permitidos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/usuarios-permitidos/{id}")).StatusCode);
    }

    [Fact]
    public async Task Teamleader_no_accede_a_la_lista_blanca()
    {
        var client = teamleader.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/usuarios-permitidos")).StatusCode);
        var crear = await client.PostAsJsonAsync("/api/usuarios-permitidos", new { email = "x@empresa.com" });
        Assert.Equal(HttpStatusCode.Forbidden, crear.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_no_accede_a_la_lista_blanca()
    {
        var client = sinSesion.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/usuarios-permitidos")).StatusCode);
    }
}
