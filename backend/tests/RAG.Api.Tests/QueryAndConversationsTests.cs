using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RAG.Domain.Models;
using RAG.Api.Tests.Infrastructure;

namespace RAG.Api.Tests;

public sealed class QueryAndConversationsTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<string> SubirYEsperarAsync(string nombre, string dominio, string contenido)
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
    public async Task Consulta_sin_documentos_se_rechaza_de_forma_controlada()
    {
        // instancia limpia: sin documentos listos
        var fresh = factory.WithWebHostBuilder(b => b.UseSetting("Storage:Provider", "InMemory"));
        var client = fresh.CreateClient();

        var response = await client.PostAsJsonAsync("/api/query", new { pregunta = "¿Cuántos días de vacaciones tengo?" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("sin_documentos", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Consulta_streaming_devuelve_meta_tokens_done_y_verified()
    {
        await SubirYEsperarAsync("politica.txt", "rrhh", "La política establece 23 días de vacaciones.");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/query",
            new { pregunta = "¿Cuántos días de vacaciones tengo?", dominios = new[] { "rrhh" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/event-stream", response.Content.Headers.ContentType?.ToString());

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: meta", body);
        Assert.Contains("event: token", body);
        Assert.Contains("La respuesta final.", body); // tokens concatenados en el stream
        Assert.Contains("event: done", body);
        Assert.Contains("\"usada\":true", body);
        Assert.Contains("event: verified", body);

        Assert.Single(factory.Rag.ChatRequests);
        Assert.Equal("rrhh", Assert.Single(factory.Rag.ChatRequests[0].Dominios!));
    }

    [Fact]
    public async Task Conversacion_flujo_completo_con_auto_titulo_y_persistencia()
    {
        await SubirYEsperarAsync("manual-onboarding.md", "onboarding", "# Bienvenida\nEl primer día preséntate en recepción.");
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/conversations",
            new { dominios = new[] { "onboarding" } });
        createResponse.EnsureSuccessStatusCode();
        var conversationId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetString()!;

        var askResponse = await client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages",
            new { pregunta = "¿Qué hago el primer día?" });
        Assert.Equal(HttpStatusCode.OK, askResponse.StatusCode);
        var sse = await askResponse.Content.ReadAsStringAsync();
        Assert.Contains("event: done", sse);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/conversations/{conversationId}", Json);
        Assert.Contains("¿Qué hago el primer día?", detail.GetProperty("conversacion").GetProperty("titulo").GetString());
        var mensajes = detail.GetProperty("mensajes");
        Assert.Equal(2, mensajes.GetArrayLength());
        Assert.Equal("user", mensajes[0].GetProperty("rol").GetString());
        Assert.Equal("assistant", mensajes[1].GetProperty("rol").GetString());
        Assert.NotNull(mensajes[1].GetProperty("fuentesJson"));
        Assert.NotNull(mensajes[1].GetProperty("trazaJson"));

        // el historial viaja al servicio RAG en la segunda pregunta
        await client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages",
            new { pregunta = "¿Y el segundo día?" });
        var secondRequest = factory.Rag.ChatRequests[^1];
        Assert.Equal(2, secondRequest.History.Count);
        Assert.Contains("primer día", secondRequest.History[0].Content);
    }

    [Fact]
    public async Task Revision_sugerida_se_acepta_y_reemplaza_contenido()
    {
        await SubirYEsperarAsync("mantenimiento.txt", "mantenimiento", "La caldera requiere revisión semestral.");
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/conversations", new { });
        var conversationId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetString()!;

        factory.Rag.ScriptedChatResponses.Enqueue(
        [
            new SseEvent("token", """{"t":"Respuesta inicial"}"""),
            new SseEvent("done", """
                {"content":"Respuesta inicial","sources":[],"trace":{"etapas":[]}}
                """),
            new SseEvent("verified", """{"verdict":"unsupported","critique":"Falta la fuente del mantenimiento"}"""),
            new SseEvent("revision_available", """{"revision":"Respuesta corregida con cita","critique":"Falta la fuente del mantenimiento"}""")
        ]);

        var ask = await client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages",
            new { pregunta = "¿Cada cuánto se revisa la caldera?" });
        ask.EnsureSuccessStatusCode();

        var messages = await client.GetFromJsonAsync<List<JsonElement>>($"/api/conversations/{conversationId}/messages", Json);
        var assistant = messages!.Single(m => m.GetProperty("rol").GetString() == "assistant");
        var messageId = assistant.GetProperty("id").GetString();
        Assert.Contains("Respuesta corregida", assistant.GetProperty("revisionContenido").GetString());

        var accept = await client.PatchAsJsonAsync(
            $"/api/conversations/{conversationId}/messages/{messageId}/revision", new { });
        accept.EnsureSuccessStatusCode();

        var updated = (await client.GetFromJsonAsync<List<JsonElement>>($"/api/conversations/{conversationId}/messages", Json))!
            .Single(m => m.GetProperty("id").GetString() == messageId);
        Assert.Equal("Respuesta corregida con cita", updated.GetProperty("contenido").GetString());
        Assert.True(updated.GetProperty("revisionContenido").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined);
    }

    [Fact]
    public async Task Evento_agent_del_director_se_reenvia_al_frontend()
    {
        await SubirYEsperarAsync("rrhh-agente.txt", "rrhh", "La política agéntica establece 24 días de vacaciones únicos.");
        var client = factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/conversations", new { });
        var conversationId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetString()!;

        factory.Rag.ScriptedChatResponses.Enqueue(
        [
            new SseEvent("agent", """{"etapa":"buscando","agente":"buscar_rrhh","query":"vacaciones"}"""),
            new SseEvent("token", """{"t":"23 días"}"""),
            new SseEvent("done", """{"content":"23 días (Fuente 1).","sources":[],"trace":{"etapas":[]}}""")
        ]);

        var ask = await client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages",
            new { pregunta = "¿Cuántos días de vacaciones?" });
        ask.EnsureSuccessStatusCode();

        var sse = await ask.Content.ReadAsStringAsync();
        Assert.Contains("event: agent", sse);
        Assert.Contains("buscar_rrhh", sse);
        // el progreso del director llega antes del primer token
        Assert.True(sse.IndexOf("event: agent", StringComparison.Ordinal) < sse.IndexOf("event: token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Overrides_de_recuperacion_fluyen_en_conversaciones()
    {
        await SubirYEsperarAsync("rrhh-overrides.txt", "rrhh", "Contenido exclusivo para verificar overrides conversacionales.");
        var client = factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/conversations", new { });
        var conversationId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetString()!;

        factory.Rag.ScriptedChatResponses.Enqueue(
        [
            new SseEvent("done", """{"content":"ok (Fuente 1).","sources":[],"trace":{"etapas":[]}}""")
        ]);

        var ask = await client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages",
            new { pregunta = "¿Cuántos días?", overridesRetrieval = new { hibrida = false, rerank = true } });
        ask.EnsureSuccessStatusCode();

        var enviado = factory.Rag.ChatRequests[^1];
        Assert.NotNull(enviado.OverridesRetrieval);
        Assert.False(enviado.OverridesRetrieval["hibrida"]);
        Assert.True(enviado.OverridesRetrieval["rerank"]);
        // el stateless /api/query también los conserva
        var stateless = await client.PostAsJsonAsync("/api/query",
            new { pregunta = "¿Cuántos días?", overridesRetrieval = new { dedupe = false } });
        stateless.EnsureSuccessStatusCode();
        Assert.False(factory.Rag.ChatRequests[^1].OverridesRetrieval!["dedupe"]);
    }

    [Fact]
    public async Task Pregunta_vacia_o_conversacion_inexistente_devuelven_error_controlado()
    {
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/conversations", new { });
        var conversationId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("id").GetString()!;

        var vacia = await client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages", new { pregunta = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, vacia.StatusCode);

        var inexistente = await client.PostAsJsonAsync($"/api/conversations/{Guid.NewGuid()}/messages", new { pregunta = "hola" });
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }
}
