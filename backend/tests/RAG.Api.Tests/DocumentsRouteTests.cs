using System.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RAG.Api.Tests.Infrastructure;

namespace RAG.Api.Tests;

public sealed class DocumentsRouteTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<HttpResponseMessage> UploadAsync(HttpClient client, string fileName, string dominio, string content)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new StringContent(content, Encoding.UTF8, "text/plain");
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(dominio), "dominio");
        return await client.PostAsync("/api/documents/upload", form);
    }

    [Fact]
    public async Task Upload_sin_archivo_devuelve_400()
    {
        var client = factory.CreateClient();
        using var form = new MultipartFormDataContent();
        var response = await client.PostAsync("/api/documents/upload", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_formato_no_soportado_devuelve_415()
    {
        var client = factory.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("MZfake", Encoding.UTF8, "application/octet-stream"), "file", "virus.exe");
        form.Add(new StringContent("rrhh"), "dominio");
        var response = await client.PostAsync("/api/documents/upload", form);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Upload_dominio_invalido_devuelve_400()
    {
        var client = factory.CreateClient();
        var response = await UploadAsync(client, "doc.txt", "finanzas", "contenido");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_txt_se_procesa_hasta_listo_y_registra_ingesta()
    {
        var client = factory.CreateClient();
        var response = await UploadAsync(client, "politica-vacaciones.txt", "rrhh",
            "Política de vacaciones: 23 días naturales por año. Se solicitan en el portal interno.");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = created.GetProperty("id").GetString()!;

        var listo = await EsperarEstadoAsync(client, id, "listo");
        Assert.True(listo, $"El documento no llegó a estado 'listo'. Estado: {await EstadoActualAsync(client, id)}");

        Assert.Single(factory.Rag.IngestRequests, r => r.DocumentoId.ToString() == id);
        var ingest = factory.Rag.IngestRequests.First(r => r.DocumentoId.ToString() == id);
        Assert.Equal(id, ingest.DocumentoId.ToString());
        Assert.Equal("rrhh", ingest.Dominio);
        Assert.Contains("vacaciones", ingest.Segmentos[0].Text);
    }

    [Fact]
    public async Task Upload_duplicado_devuelve_409()
    {
        var client = factory.CreateClient();
        const string content = "Manual de mantenimiento preventivo: revisión trimestral de equipos.";
        await EsperarEstadoAsync(client,
            (await (await UploadAsync(client, "manual.txt", "mantenimiento", content)).Content
                .ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!, "listo");

        var segunda = await UploadAsync(client, "manual-copia.txt", "mantenimiento", content);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Contains("manual.txt", await segunda.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Delete_documento_limpia_indice_remoto()
    {
        var client = factory.CreateClient();
        var upload = await UploadAsync(client, "onboarding-checklist.md", "onboarding",
            "# Checklist onboarding\n- Alta en sistemas\n- Presentación de equipo");
        var id = (await upload.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;
        await EsperarEstadoAsync(client, id, "listo");

        var delete = await client.DeleteAsync($"/api/documents/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Contains(Guid.Parse(id), factory.Rag.DeletedDocuments);

        var listado = await client.GetFromJsonAsync<List<JsonElement>>("/api/documents", Json);
        Assert.DoesNotContain(listado!, d => d.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task Listado_filtra_por_dominio_y_nombre()
    {
        var client = factory.CreateClient();
        await EsperarEstadoAsync(client,
            (await (await UploadAsync(client, "nomina-info.txt", "rrhh", "Calendario de nómina mensual.")).Content
                .ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!, "listo");

        var rrhh = await client.GetFromJsonAsync<List<JsonElement>>("/api/documents?dominio=rrhh", Json);
        Assert.All(rrhh!, d => Assert.Equal("rrhh", d.GetProperty("dominio").GetString()));

        var porNombre = await client.GetFromJsonAsync<List<JsonElement>>("/api/documents?q=nomina", Json);
        Assert.NotEmpty(porNombre!);
    }

    private static async Task<bool> EsperarEstadoAsync(HttpClient client, string id, string esperado)
    {
        for (var i = 0; i < 100; i++)
        {
            if ((await EstadoActualAsync(client, id)) == esperado) return true;
            await Task.Delay(50);
        }
        return false;
    }

    private static async Task<string> EstadoActualAsync(HttpClient client, string id)
    {
        var status = await client.GetFromJsonAsync<JsonElement>($"/api/documents/{id}/status", Json);
        return status.GetProperty("estado").GetString() ?? "";
    }
}
