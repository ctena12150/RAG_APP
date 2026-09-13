using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RAG.Api.Tests.Infrastructure;

namespace RAG.Api.Tests;

/// <summary>
/// La traza técnica del pipeline solo llega al superusuario: el resto la recibe
/// a null tanto en el SSE en vivo como en los mensajes persistidos.
/// </summary>
public sealed class TrazaGatingTests(
    AuthTeamLeaderApiTestFactory equipo,
    AuthSuperUsuarioApiTestFactory super) : IClassFixture<AuthTeamLeaderApiTestFactory>, IClassFixture<AuthSuperUsuarioApiTestFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<string> SubirYEsperarAsync(HttpClient client, string nombre, string dominio, string contenido)
    {
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
    public async Task Teamleader_no_recibe_traza_en_sse_ni_en_historial()
    {
        var client = equipo.CreateClient();
        await SubirYEsperarAsync(client, "guia-traza.txt", "rrhh", "La guía establece 23 días de vacaciones.");

        var createResponse = await client.PostAsJsonAsync("/api/conversations", new { });
        createResponse.EnsureSuccessStatusCode();
        var conversationId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetString()!;

        var askResponse = await client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages",
            new { pregunta = "¿Cuántos días tengo?" });
        askResponse.EnsureSuccessStatusCode();
        var sse = await askResponse.Content.ReadAsStringAsync();
        Assert.Contains("event: done", sse);
        Assert.DoesNotContain("etapas", sse);

        var mensajes = await client.GetFromJsonAsync<JsonElement>(
            $"/api/conversations/{conversationId}/messages", Json);
        var asistente = mensajes.EnumerateArray().First(m => m.GetProperty("rol").GetString() == "assistant");
        Assert.Equal(JsonValueKind.Null, asistente.GetProperty("trazaJson").ValueKind);
    }

    [Fact]
    public async Task Superusuario_si_recibe_traza_en_sse_y_en_historial()
    {
        var client = super.CreateClient();
        await SubirYEsperarAsync(client, "guia-traza-super.txt", "rrhh", "La guía establece 23 días de vacaciones.");

        var createResponse = await client.PostAsJsonAsync("/api/conversations", new { });
        createResponse.EnsureSuccessStatusCode();
        var conversationId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetString()!;

        var askResponse = await client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages",
            new { pregunta = "¿Cuántos días tengo?" });
        askResponse.EnsureSuccessStatusCode();
        var sse = await askResponse.Content.ReadAsStringAsync();
        Assert.Contains("event: done", sse);
        Assert.Contains("etapas", sse);

        var mensajes = await client.GetFromJsonAsync<JsonElement>(
            $"/api/conversations/{conversationId}/messages", Json);
        var asistente = mensajes.EnumerateArray().First(m => m.GetProperty("rol").GetString() == "assistant");
        Assert.Equal(JsonValueKind.String, asistente.GetProperty("trazaJson").ValueKind);
        Assert.Contains("etapas", asistente.GetProperty("trazaJson").GetString());
    }
}
