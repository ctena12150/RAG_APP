import { describe, expect, it, vi } from "vitest";
import { describirAgente, streamChat } from "../lib/api";

describe("streamChat: evento agent", () => {
  it("reparte los eventos agent al handler onAgent en orden", async () => {
    const sse = [
      "event: meta",
      `data: {"messageId":"m1","conversationId":"c1"}`,
      "",
      "event: agent",
      `data: {"etapa":"planificacion","documentos":2}`,
      "",
      "event: agent",
      `data: {"etapa":"buscando","agente":"buscar_rrhh","query":"vacaciones"}`,
      "",
      "event: token",
      `data: {"t":"23 días"}`,
      "",
      "event: done",
      `data: {"messageId":"m1","content":"23 días (Fuente 1).","sources":[],"trace":null}`,
      "",
    ].join("\n\n");

    const fetchMock = vi.fn().mockResolvedValue(
      new Response(sse, { status: 200, headers: { "Content-Type": "text/event-stream" } }),
    );
    vi.stubGlobal("fetch", fetchMock);

    const agentes: string[] = [];
    let tokens = 0;
    await streamChat("/api/x", {}, {
      onAgent(p) {
        agentes.push(describirAgente(p));
      },
      onToken() {
        tokens += 1;
      },
      onDone() {
        tokens += 100;
      },
    });

    expect(agentes).toEqual([
      "Director planificando (2 documentos)…",
      'buscar_rrhh: "vacaciones"',
    ]);
    expect(tokens).toBe(101); // 1 token + done
    vi.unstubAllGlobals();
  });
});

describe("describirAgente", () => {
  it("traduce las tres etapas del director", () => {
    expect(describirAgente({ etapa: "planificacion", documentos: 3 })).toContain("Director");
    expect(describirAgente({ etapa: "buscando", agente: "buscar_mantenimiento", query: "caldera" })).toContain("caldera");
    expect(describirAgente({ etapa: "hallazgo", agente: "buscar_rrhh", pasajes: 4 })).toContain("4 pasajes");
    expect(describirAgente({ etapa: "otra" as never })).toBe("");
  });
});
