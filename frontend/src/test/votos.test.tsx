import { cleanup, render, screen, fireEvent } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import MessageBubble from "../components/MessageBubble";
import type { MensajeChat } from "../lib/types";

const votarMensaje = vi.fn(async () => { });

vi.mock("../state/AppContext", () => ({
  useApp: () => ({
    fuentesSeleccionadas: null,
    seleccionarFuentes: vi.fn(),
    aceptarRevision: vi.fn(),
    preguntar: vi.fn(),
    votarMensaje,
    chat: { enviando: false },
  }),
}));

vi.mock("../lib/voz", () => ({
  soportaSintesis: () => false,
  copiarAlPortapapeles: vi.fn(async () => true),
  leerEnVozAlta: vi.fn(),
  textoPlanoMarkdown: (t: string) => t,
  detenerLectura: vi.fn(),
}));

describe("VotosMensaje", () => {
  afterEach(() => {
    cleanup();
    votarMensaje.mockClear();
  });

  it("muestra pulgares y guarda el voto una sola vez", () => {
    const mensaje: MensajeChat = { id: "m-voto", rol: "assistant", contenido: "Respuesta.", pendiente: false };
    render(<MessageBubble mensaje={mensaje} />);
    const bien = screen.getByRole("button", { name: "Sí me sirvió" });
    const mal = screen.getByRole("button", { name: "No me sirvió" });
    expect(bien).toBeInTheDocument();
    expect(mal).toBeInTheDocument();
    fireEvent.click(bien);
    expect(votarMensaje).toHaveBeenCalledWith("m-voto", "bien");
  });

  it("no muestra pulgares en mensajes temporales", () => {
    const mensaje: MensajeChat = { id: "tmp-1", rol: "assistant", contenido: "Borrador.", pendiente: false };
    render(<MessageBubble mensaje={mensaje} />);
    expect(screen.queryByRole("button", { name: "Sí me sirvió" })).not.toBeInTheDocument();
  });
});
