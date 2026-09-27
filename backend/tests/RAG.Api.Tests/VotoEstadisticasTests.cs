using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RAG.Api.Tests.Infrastructure;

namespace RAG.Api.Tests;

public sealed class VotoEstadisticasTests(
    ApiTestFactory basica,
    AuthApiTestFactory usuario,
    AuthSuperUsuarioApiTestFactory superusuario) :
    IClassFixture<ApiTestFactory>,
    IClassFixture<AuthApiTestFactory>,
    IClassFixture<AuthSuperUsuarioApiTestFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<string> SubirYEsperarAsync(ApiTestFactory factory, string nombre, string dominio, string contenido)
    {
        var client = factory.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(contenido, Encoding.UTF8, "text/plain"), "file", nombre);
        form.Add(new StringContent(dominio), "dominio");
        var upload = await client.PostAsync("/api/documents/upload", form);
        upload.EnsureSuccessStatusCode();
        var id = (await upload.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;
        for (var i = 0; i < 100; i++)
        {
            var status = await client.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/status", Json);
            if (status.GetProperty("estado").GetString() == "listo") return id;
            await Task.Delay(50);
        }
        throw new TimeoutException("El documento no se procesó a tiempo.");
    }

    [Fact]
    public async Task Voto_bien_se_guarda_y_rechaza_voto_invalido()
    {
        await SubirYEsperarAsync(basica, "voto.txt", "rrhh", "La política establece 23 días de vacaciones.");
        var client = basica.CreateClient();
        var creada = await client.PostAsJsonAsync("/api/conversations", new { dominios = new[] { "rrhh" } });
        creada.EnsureSuccessStatusCode();
        var convId = (await creada.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;
        var ask = await client.PostAsJsonAsync($"/api/conversations/{convId}/messages", new { pregunta = "¿Cuántos días tengo?" });
        Assert.Equal(HttpStatusCode.OK, ask.StatusCode);

        var detalle = await client.GetFromJsonAsync<JsonElement>($"/api/conversations/{convId}", Json);
        var assistantId = detalle.GetProperty("mensajes").EnumerateArray()
            .First(m => m.GetProperty("rol").GetString() == "assistant")
            .GetProperty("id").GetString()!;

        var voto = await client.PatchAsJsonAsync($"/api/conversations/{convId}/messages/{assistantId}/voto", new { voto = "bien" });
        Assert.Equal(HttpStatusCode.OK, voto.StatusCode);

        var detalle2 = await client.GetFromJsonAsync<JsonElement>($"/api/conversations/{convId}", Json);
        var votoGuardado = detalle2.GetProperty("mensajes").EnumerateArray()
            .First(m => m.GetProperty("id").GetString() == assistantId)
            .GetProperty("voto").GetString();
        Assert.Equal("bien", votoGuardado);

        var invalido = await client.PatchAsJsonAsync($"/api/conversations/{convId}/messages/{assistantId}/voto", new { voto = "quizas" });
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }

    [Fact]
    public async Task Estadisticas_muestras_contienen_la_pregunta_no_la_respuesta()
    {
        var admin = superusuario.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("La política establece 23 días de vacaciones.", Encoding.UTF8, "text/plain"), "file", "pareja.txt");
        form.Add(new StringContent("rrhh"), "dominio");
        var upload = await admin.PostAsync("/api/documents/upload", form);
        upload.EnsureSuccessStatusCode();
        var docId = (await upload.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;
        for (var i = 0; i < 100; i++)
        {
            var status = await admin.GetFromJsonAsync<JsonElement>($"/api/documents/{docId}/status", Json);
            if (status.GetProperty("estado").GetString() == "listo") break;
            await Task.Delay(50);
        }

        const string pregunta = "¿Pregunta de emparejamiento única 7391?";
        var creada = await admin.PostAsJsonAsync("/api/conversations", new { dominios = new[] { "rrhh" } });
        creada.EnsureSuccessStatusCode();
        var convId = (await creada.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;
        var ask = await admin.PostAsJsonAsync($"/api/conversations/{convId}/messages", new { pregunta });
        Assert.Equal(HttpStatusCode.OK, ask.StatusCode);

        var detalle = await admin.GetFromJsonAsync<JsonElement>($"/api/conversations/{convId}", Json);
        var assistantId = detalle.GetProperty("mensajes").EnumerateArray()
            .First(m => m.GetProperty("rol").GetString() == "assistant")
            .GetProperty("id").GetString()!;
        var voto = await admin.PatchAsJsonAsync($"/api/conversations/{convId}/messages/{assistantId}/voto", new { voto = "mal" });
        Assert.Equal(HttpStatusCode.OK, voto.StatusCode);

        var stats = await admin.GetFromJsonAsync<JsonElement>("/api/estadisticas", Json);
        var muestra = stats.GetProperty("ultimasMalas").EnumerateArray()
            .First(m => m.GetProperty("pregunta").GetString()!.Contains("7391"));
        Assert.Contains("7391", muestra.GetProperty("pregunta").GetString());
        Assert.DoesNotContain("La respuesta final", muestra.GetProperty("pregunta").GetString());
    }

    [Fact]
    public async Task Estadisticas_usuario_normal_recibe_403_y_superusuario_200()
    {
        var comun = usuario.CreateClient();
        var denegado = await comun.GetAsync("/api/estadisticas");
        Assert.Equal(HttpStatusCode.Forbidden, denegado.StatusCode);

        var admin = superusuario.CreateClient();
        var ok = await admin.GetAsync("/api/estadisticas");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var cuerpo = await ok.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(cuerpo.TryGetProperty("totalConsultas", out _));
        Assert.True(cuerpo.TryGetProperty("porDominio", out _));
    }
}
