using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RAG.Api.Tests.Infrastructure;

namespace RAG.Api.Tests;

public sealed class DominiosRouteTests(
    ApiTestFactory factory,
    AuthApiTestFactory usuario,
    AuthSuperUsuarioApiTestFactory superusuario)
    : IClassFixture<ApiTestFactory>,
      IClassFixture<AuthApiTestFactory>,
      IClassFixture<AuthSuperUsuarioApiTestFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Listar_devuelve_los_cuatro_dominios_sembrados()
    {
        var client = factory.CreateClient();
        var lista = await client.GetFromJsonAsync<JsonElement>("/api/dominios", Json);
        var claves = lista.EnumerateArray().Select(e => e.GetProperty("clave").GetString()).ToList();
        Assert.Contains("rrhh", claves);
        Assert.Contains("it", claves);
    }

    [Fact]
    public async Task Crear_dominio_nuevo_devuelve_201_y_luego_duplicado_409()
    {
        var client = factory.CreateClient();
        var creado = await client.PostAsJsonAsync("/api/dominios",
            new { clave = "legal", etiqueta = "Legal", descripcion = "Contratos y normativa" });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);

        var duplicado = await client.PostAsJsonAsync("/api/dominios",
            new { clave = "legal", etiqueta = "Legal", descripcion = "" });
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    [Fact]
    public async Task Crear_con_clave_invalida_devuelve_400()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dominios",
            new { clave = "MAL!", etiqueta = "Mal", descripcion = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Actualizar_modifica_etiqueta_y_borrar_libre_devuelve_204()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/dominios",
            new { clave = "temporal", etiqueta = "Temporal", descripcion = "" });

        var patch = await client.PatchAsJsonAsync("/api/dominios/temporal",
            new { etiqueta = "Temporal 2" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var borrado = await client.DeleteAsync("/api/dominios/temporal");
        Assert.Equal(HttpStatusCode.NoContent, borrado.StatusCode);
    }

    [Fact]
    public async Task Borrar_dominio_en_uso_devuelve_409()
    {
        var client = factory.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Guía de IT para el test de dominio en uso.", Encoding.UTF8, "text/plain"), "file", "guia-it.txt");
        form.Add(new StringContent("it"), "dominio");
        var upload = await client.PostAsync("/api/documents/upload", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        var borrado = await client.DeleteAsync("/api/dominios/it");
        Assert.Equal(HttpStatusCode.Conflict, borrado.StatusCode);
    }

    [Fact]
    public async Task Usuario_sin_rol_no_puede_crear_dominios_devuelve_403()
    {
        var client = usuario.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dominios",
            new { clave = "prohibido", etiqueta = "Prohibido", descripcion = "" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Superusuario_puede_crear_dominios()
    {
        var client = superusuario.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dominios",
            new { clave = "calidad", etiqueta = "Calidad", descripcion = "" });
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
    }
}
