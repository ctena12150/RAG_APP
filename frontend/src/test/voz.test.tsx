import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import ChatPanel from "../components/ChatPanel";
import { useApp } from "../state/AppContext";
import { componerTextoConversacion, limpiarTextoParaVoz } from "../lib/voz";
import type { MensajeChat } from "../lib/types";

vi.mock("../lib/api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../lib/api")>()), // dividirPorCitas real (la usa MessageBubble)
  api: {
    mensajesDe: vi.fn(async () => []),
    listarModelos: vi.fn(async () => []),
  },
}));

vi.mock("../state/AppContext", () => ({
  useApp: vi.fn(),
}));

const useAppMock = vi.mocked(useApp);

describe("limpiarTextoParaVoz / componerTextoConversacion", () => {
  it("limpia citas, markdown y cÃ³digo para que la voz suene natural", () => {
    const limpio = limpiarTextoParaVoz("Tiene **23 dÃ­as** (Fuente 1).\n\n```bash\nnpm i\n```\nVer `README.md`.");
    expect(limpio).not.toContain("Fuente 1");
    expect(limpio).not.toContain("**");
    expect(limpio).not.toContain("```");
    expect(limpio).toContain("Tiene 23 dÃ­as.");
    expect(limpio).toContain("Ver README.md.");
  });

  it("compone el guion pregunta/respuesta omitiendo turnos vacÃ­os", () => {
    const mensajes = [
      { id: "1", rol: "user", contenido: "Hola", pendiente: false },
      { id: "2", rol: "assistant", contenido: "", pendiente: true },
      { id: "3", rol: "assistant", contenido: "Son 23 dÃ­as", pendiente: false },
    ] as MensajeChat[];
    expect(componerTextoConversacion(mensajes)).toBe("Pregunta: Hola Respuesta: Son 23 dÃ­as");
  });
});


describe("ChatPanel Â· dictado por voz", () => {
  beforeEach(() => {
    Element.prototype.scrollIntoView = vi.fn();
    useAppMock.mockReturnValue({
      chat: { enviando: false, mensajes: [], error: null, actividad: [] },
      preguntar: vi.fn(async () => {}),
      detenerGeneracion: vi.fn(),
      conversacionActiva: null,
      documentos: [],
    } as unknown as ReturnType<typeof useApp>);
  });

  afterEach(() => {
    cleanup();
    delete (window as unknown as { SpeechRecognition?: unknown }).SpeechRecognition;
    delete (window as unknown as { webkitSpeechRecognition?: unknown }).webkitSpeechRecognition;
  });

  it("oculta el micrÃ³fono cuando el navegador no soporta dictado", async () => {
    render(<ChatPanel />);
    await act(async () => {});
    expect(screen.queryByRole("button", { name: "Dictar por voz" })).not.toBeInTheDocument();
  });

  it("aÃ±ade el transcript al composer sin borrar lo escrito y permite detener", async () => {
    const creados: Array<{
      lang: string;
      start: () => void;
      stop: () => void;
      abort: () => void;
      onresult: ((evento: unknown) => void) | null;
    }> = [];
    class ReconocimientoFalso {
      lang = "";
      continuous = false;
      interimResults = false;
      onresult: ((evento: unknown) => void) | null = null;
      onend: (() => void) | null = null;
      onerror: ((evento: unknown) => void) | null = null;
      start() {
        creados.push(this);
      }
      stop() {}
      abort() {}
    }
    (window as unknown as { SpeechRecognition: unknown }).SpeechRecognition = ReconocimientoFalso;

    render(<ChatPanel />);
    await act(async () => {});
    const composer = screen.getByTestId("composer") as HTMLTextAreaElement;
    fireEvent.change(composer, { target: { value: "Sobre el manual:" } });
    fireEvent.click(screen.getByRole("button", { name: "Dictar por voz" }));
    expect(creados).toHaveLength(1);
    expect(creados[0].lang).toBe("es-ES");

    await act(async () => {
      creados[0].onresult?.({
        resultIndex: 0,
        results: [{ isFinal: true, 0: { transcript: " cuÃ¡ntos dÃ­as de vacaciones " } }],
      });
    });
    expect(composer.value).toBe("Sobre el manual: cuÃ¡ntos dÃ­as de vacaciones");

    const espiaStop = vi.spyOn(creados[0], "stop");
    fireEvent.click(screen.getByRole("button", { name: "Detener dictado" }));
    expect(espiaStop).toHaveBeenCalledOnce();
  });

  it("descarta los resultados parciales para no duplicar texto", async () => {
    const creados: Array<{ start: () => void; onresult: ((evento: unknown) => void) | null }> = [];
    class ReconocimientoFalso {
      lang = "";
      continuous = false;
      interimResults = false;
      onresult: ((evento: unknown) => void) | null = null;
      onend: (() => void) | null = null;
      onerror: ((evento: unknown) => void) | null = null;
      start() {
        creados.push(this);
      }
      stop() {}
      abort() {}
    }
    (window as unknown as { webkitSpeechRecognition: unknown }).webkitSpeechRecognition = ReconocimientoFalso;

    render(<ChatPanel />);
    await act(async () => {});
    fireEvent.click(screen.getByRole("button", { name: "Dictar por voz" }));

    await act(async () => {
      creados[0].onresult?.({
        resultIndex: 0,
        results: [{ isFinal: false, 0: { transcript: "parcial que no cuenta" } }],
      });
    });
    const composer = screen.getByTestId("composer") as HTMLTextAreaElement;
    expect(composer.value).toBe("");
  });
});

