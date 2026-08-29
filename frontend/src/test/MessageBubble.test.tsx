import { act, cleanup, render, screen, fireEvent } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import MessageBubble from "../components/MessageBubble";
import type { MensajeChat } from "../lib/types";

vi.mock("../state/AppContext", () => ({
  useApp: () => ({
    fuentesSeleccionadas: null,
    seleccionarFuentes: vi.fn(),
    aceptarRevision: vi.fn(),
  }),
}));

vi.mock("../lib/voz", () => ({
  soportaSintesis: vi.fn(() => true),
  copiarAlPortapapeles: vi.fn(async () => true),
  leerEnVozAlta: vi.fn(),
  textoPlanoMarkdown: vi.fn((t: string) => t),
  detenerLectura: vi.fn(),
}));

function instalarSintesis() {
  const cancel = vi.fn();
  Object.defineProperty(window, "speechSynthesis", {
    configurable: true,
    value: { cancel, getVoices: vi.fn(() => [{ lang: "es-ES", name: "Mónica" }]) },
  });
  Object.defineProperty(window, "SpeechSynthesisUtterance", {
    configurable: true,
    value: class {
      text: string;
      lang = "";
      constructor(_texto: string) { this.text = _texto; }
    },
  });
}

describe("MessageBubble", () => {
  beforeEach(() => {
    Element.prototype.scrollIntoView = vi.fn();
    instalarSintesis();
  });

  afterEach(() => {
    cleanup();
    delete (window as unknown as { speechSynthesis?: unknown }).speechSynthesis;
    delete (window as unknown as { SpeechSynthesisUtterance?: unknown }).SpeechSynthesisUtterance;
  });

  it("renderiza la respuesta con sellos de cita usados vs recuperadas", () => {
    const mensaje: MensajeChat = {
      id: "m",
      rol: "assistant",
      contenido: "Hay 23 días (Fuente 1).",
      fuentes: [
        {
          indice: 1, documentoId: "d", documentoNombre: "politica.pdf", chunkId: "c1",
          chunkIndice: 0, pagina: 2, seccion: null, fragmento: "Son 23 días naturales…",
          puntuacion: 0.92, usada: true,
        },
        {
          indice: 2, documentoId: "d", documentoNombre: "otro.pdf", chunkId: "c2",
          chunkIndice: 3, pagina: null, seccion: null, fragmento: "Texto recuperado…",
          puntuacion: 0.4, usada: false,
        },
      ],
      traza: { modo: "fijo", etapas: [{ etapa: "expansion", duracionMs: 120 }, { etapa: "busqueda_hibrida", duracionMs: 80 }] },
      verificacion: { verdict: "supported" },
      pendiente: false,
    };

    render(<MessageBubble mensaje={mensaje} />);
    expect(screen.getByText(/Hay 23 días/)).toBeInTheDocument();
    const sello1 = screen.getByTitle(/politica\.pdf · pág\. 2/);
    expect(sello1).toHaveAttribute("data-used", "true");
    expect(screen.queryByTitle(/otro\.pdf/)).not.toBeInTheDocument();
    expect(screen.getByText(/Verificado/)).toBeInTheDocument();
    expect(screen.getByText(/Traza \(fijo\)/)).toBeInTheDocument();
  });

  it("muestra banner de verificación fallida y sugerencia de revisión", () => {
    const mensaje: MensajeChat = {
      id: "m2", rol: "assistant", contenido: "Respuesta dudosa (Fuente 1).",
      fuentes: [{ indice: 1, documentoId: "d", documentoNombre: "a.pdf", chunkId: "c", chunkIndice: 0, pagina: 1, seccion: null, fragmento: "x", puntuacion: 0.5, usada: true }],
      verificacion: { verdict: "unsupported", critique: "afirma un número sin fuente" },
      revisionContenido: "Versión corregida.",
      pendiente: false,
    };

    render(<MessageBubble mensaje={mensaje} />);
    expect(screen.getByText(/⚠ Revisar/)).toBeInTheDocument();
    expect(screen.getByTestId("revision-sugerida")).toHaveTextContent("Versión corregida.");
    expect(screen.getByRole("button", { name: /Aplicar revisión/ })).toBeInTheDocument();
  });

  it("estado pendiente muestra indicador y no sellos", () => {
    const mensaje: MensajeChat = { id: "m3", rol: "assistant", contenido: "", pendiente: true };
    render(<MessageBubble mensaje={mensaje} />);
    expect(screen.getByLabelText("generando respuesta")).toBeInTheDocument();
    expect(document.querySelectorAll(".citation-stamp")).toHaveLength(0);
  });

  describe("AccionesMensaje", () => {
    it("muestra botones Escuchar y Copiar en respuesta del asistente", () => {
      const mensaje: MensajeChat = {
        id: "m4", rol: "assistant", contenido: "Respuesta de prueba (Fuente 1).",
        pendiente: false,
      };
      render(<MessageBubble mensaje={mensaje} />);
      expect(screen.getByRole("button", { name: /Escuchar respuesta/ })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /Copiar respuesta/ })).toBeInTheDocument();
    });

    it("no muestra acciones en mensaje de usuario", () => {
      const mensaje: MensajeChat = {
        id: "m5", rol: "user", contenido: "Hola", pendiente: false,
      };
      render(<MessageBubble mensaje={mensaje} />);
      expect(screen.queryByRole("button", { name: /Escuchar respuesta/ })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /Copiar respuesta/ })).not.toBeInTheDocument();
    });

    it("no muestra acciones en mensaje pendiente sin contenido", () => {
      const mensaje: MensajeChat = { id: "m6", rol: "assistant", contenido: "", pendiente: true };
      render(<MessageBubble mensaje={mensaje} />);
      expect(screen.queryByRole("button", { name: /Escuchar respuesta/ })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /Copiar respuesta/ })).not.toBeInTheDocument();
    });

    it("al copiar muestra feedback Copiado ✓ temporalmente", async () => {
      const mensaje: MensajeChat = { id: "m7", rol: "assistant", contenido: "Texto a copiar", pendiente: false };
      render(<MessageBubble mensaje={mensaje} />);
      fireEvent.click(screen.getByRole("button", { name: /Copiar respuesta/ }));
      expect(await screen.findByText("✓ Copiado")).toBeInTheDocument();
      await act(async () => { await new Promise((r) => setTimeout(r, 2100)); });
      expect(screen.queryByText("✓ Copiado")).not.toBeInTheDocument();
    });

    it("al escuchar muestra Detener y usa el id del mensaje", () => {
      const mensaje: MensajeChat = { id: "m8", rol: "assistant", contenido: "Texto para escuchar", pendiente: false };
      render(<MessageBubble mensaje={mensaje} />);
      fireEvent.click(screen.getByRole("button", { name: "Escuchar respuesta" }));
      expect(screen.getByRole("button", { name: "Detener lectura" })).toBeInTheDocument();
    });

    it("al detener la lectura restaura el botón Escuchar", () => {
      const mensaje: MensajeChat = { id: "m9", rol: "assistant", contenido: "Texto", pendiente: false };
      render(<MessageBubble mensaje={mensaje} />);
      const btnEscuchar = screen.getByRole("button", { name: "Escuchar respuesta" });
      fireEvent.click(btnEscuchar);
      expect(screen.getByRole("button", { name: "Detener lectura" })).toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "Detener lectura" }));
      expect(screen.getByRole("button", { name: "Escuchar respuesta" })).toBeInTheDocument();
    });
  });
});