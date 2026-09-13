using RAG.Domain.Models;
using RAG.Infrastructure;
using RAG.Infrastructure.Stores.InMemory;

namespace RAG.Api.Tests;

public sealed class StoresTests
{
    private static Document Nuevo(string nombre, string hash) => new()
    {
        Id = Guid.NewGuid(),
        NombreArchivo = nombre,
        Dominio = "rrhh",
        TamanoBytes = 10,
        ContentHash = hash,
        Estado = DocumentStatus.Pendiente,
        CreadoUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task Create_segundo_con_mismo_hash_lanza_documento_duplicado()
    {
        var store = new InMemoryDocumentStore();
        await store.CreateAsync(Nuevo("a.txt", "abc"));
        await Assert.ThrowsAsync<DocumentoDuplicadoException>(() => store.CreateAsync(Nuevo("b.txt", "abc")));
    }

    [Fact]
    public async Task HayListos_y_Contar_sin_descargar_la_tabla()
    {
        var store = new InMemoryDocumentStore();
        Assert.False(await store.HayListosAsync());
        var doc = Nuevo("a.txt", "abc");
        await store.CreateAsync(doc);
        Assert.False(await store.HayListosAsync());
        await store.UpdateEstadoAsync(doc.Id, DocumentStatus.Listo);
        Assert.True(await store.HayListosAsync());
        Assert.Equal((1, 1), await store.ContarAsync());
    }

    [Fact]
    public async Task UnassignFolder_desasigna_todos_en_una_llamada()
    {
        var store = new InMemoryDocumentStore();
        var folder = Guid.NewGuid();
        var a = Nuevo("a.txt", "a"); a.FolderId = folder;
        var b = Nuevo("b.txt", "b"); b.FolderId = folder;
        await store.CreateAsync(a);
        await store.CreateAsync(b);
        await store.UnassignFolderAsync(folder);
        Assert.Null((await store.FindByIdAsync(a.Id))!.FolderId);
        Assert.Null((await store.FindByIdAsync(b.Id))!.FolderId);
    }

    [Fact]
    public async Task ListRecientes_devuelve_solo_los_ultimos_en_orden()
    {
        var store = new InMemoryMessageStore();
        var conv = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            await store.AddAsync(new Message
            {
                Id = Guid.NewGuid(), ConversacionId = conv, Rol = "user",
                Contenido = $"m{i}", CreadoUtc = DateTime.UtcNow.AddSeconds(i)
            });
        var recientes = await store.ListRecientesAsync(conv, 2);
        Assert.Equal(2, recientes.Count);
        Assert.Equal("m3", recientes[0].Contenido);
        Assert.Equal("m4", recientes[1].Contenido);
    }

    [Fact]
    public async Task ApplyRevision_devuelve_el_contenido_en_una_llamada()
    {
        var store = new InMemoryMessageStore();
        var id = Guid.NewGuid();
        await store.AddAsync(new Message
        {
            Id = id, ConversacionId = Guid.NewGuid(), Rol = "assistant",
            Contenido = "viejo", CreadoUtc = DateTime.UtcNow
        });
        var contenido = await store.ApplyRevisionAsync(id, "nuevo");
        Assert.Equal("nuevo", contenido);
        Assert.Equal("nuevo", (await store.FindByIdAsync(id))!.Contenido);
    }

    [Fact]
    public async Task ObtenerPorId_devuelve_usuario_sin_listar_todo()
    {
        var store = new InMemoryUsuarioLocalStore();
        var creado = await store.CrearAsync(new UsuarioLocal
        {
            Id = Guid.NewGuid(), Usuario = "ana", PasswordHash = "h",
            Rol = "usuario", Dominios = [], Activo = true, CreadoUtc = DateTime.UtcNow
        });
        var obtenido = await store.ObtenerPorIdAsync(creado.Id);
        Assert.NotNull(obtenido);
        Assert.Equal("ana", obtenido.Usuario);
        Assert.Null(await store.ObtenerPorIdAsync(Guid.NewGuid()));
    }
}
