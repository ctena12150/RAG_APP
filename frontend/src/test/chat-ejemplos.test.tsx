import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import ChatPanel from "../components/ChatPanel";

vi.mock("../state/AppContext", () => ({ useApp: vi.fn() }));
vi.mock("../lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../lib/api")>();
  return { ...actual, api: { ...actual.api, listarModelos: vi.fn().mockResolvedValue([]) } };
});
vi.mock("../lib/voz", () => ({
  useDictado: () => ({ grabando: false, error: null, iniciar: vi.fn(), detener: vi.fn() }),
  soportaDictado: () => false,
}));
import { useApp } from "../state/AppContext";

function mockear(supero: Record<string, unknown> = {}) {
  const preguntar = vi.fn().mockResolvedValue(undefined);
  vi.mocked(useApp).mockReturnValue({
    chat: { mensajes: [], enviando: false, error: null, actividad: [], metricas: null },
    preguntar,
    detenerGeneracion: vi.fn(),
    conversacionActiva: null,
    documentos: [{ id: "d1", estado: "listo" }],
    dominios: [
      { clave: "rrhh", etiqueta: "Recursos Humanos", ejemplos: ["¿Cuántos días de vacaciones tengo?"] },
      { clave: "it", etiqueta: "IT", ejemplos: ["¿Cuál es la política de contraseñas?"] },
    ],
    etiquetaDominio: (c: string) => c,
    ...supero,
  } as unknown as ReturnType<typeof useApp>);
  return preguntar;
}

describe("ChatPanel · ejemplos por dominio", () => {
  beforeEach(() => {
    Element.prototype.scrollIntoView = vi.fn();
  });

  it("muestra la unión de ejemplos de los dominios seleccionados", () => {
    mockear();
    localStorage.setItem("rag-dominios-chat", JSON.stringify(["rrhh", "it"]));
    render(<ChatPanel />);
    expect(screen.getByRole("button", { name: "¿Cuántos días de vacaciones tengo?" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "¿Cuál es la política de contraseñas?" })).toBeInTheDocument();
  });

  it("con Todas no muestra chips", () => {
    mockear();
    localStorage.setItem("rag-dominios-chat", JSON.stringify([]));
    render(<ChatPanel />);
    expect(screen.queryByRole("button", { name: "¿Cuántos días de vacaciones tengo?" })).not.toBeInTheDocument();
  });

  it("al pulsar un chip inicia la conversación con el ejemplo", () => {
    const preguntar = mockear();
    localStorage.setItem("rag-dominios-chat", JSON.stringify(["it"]));
    render(<ChatPanel />);
    fireEvent.click(screen.getByRole("button", { name: "¿Cuál es la política de contraseñas?" }));
    expect(preguntar).toHaveBeenCalledWith(
      "¿Cuál es la política de contraseñas?",
      undefined,
      expect.anything(),
      expect.anything(),
      ["it"],
    );
  });
});
