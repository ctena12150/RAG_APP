import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import MessageBubble from "../components/MessageBubble";
import type { MensajeChat } from "../lib/types";

vi.mock("../state/AppContext", () => ({
  useApp: () => ({
    fuentesSeleccionadas: null,
    seleccionarFuentes: vi.fn(),
    aceptarRevision: vi.fn(),
  }),
}));

describe("MessageBubble", () => {
  it("renderiza la respuesta con sellos de cita usados vs recuperadas", () => {
    const mensaje: MensajeChat = {
      id: "m",
      rol: "assistant",
      contenido: "Hay 23 días (Fuente 1).",
      fuentes: [
        {
          indice: 1,
          documentoId: "d",
          documentoNombre: "politica.pdf",
          chunkId: "c1",
          chunkIndice: 0,
          pagina: 2,
          seccion: null,
          fragmento: "Son 23 días naturales…",
          puntuacion: 0.92,
          usada: true,
        },
        {
          indice: 2,
          documentoId: "d",
          documentoNombre: "otro.pdf",
          chunkId: "c2",
          chunkIndice: 3,
          pagina: null,
          seccion: null,
          fragmento: "Texto recuperado…",
          puntuacion: 0.4,
          usada: false,
        },
      ],
      traza: {
        modo: "fijo",
        etapas: [
          { etapa: "expansion", duracionMs: 120 },
          { etapa: "busqueda_hibrida", duracionMs: 80 },
        ],
      },
      verificacion: { verdict: "supported" },
      pendiente: false,
    };

    render(<MessageBubble mensaje={mensaje} />);
    expect(screen.getByText(/Hay 23 días/)).toBeInTheDocument();
    const sello1 = screen.getByTitle(/politica\.pdf · pág\. 2/);
    expect(sello1).toHaveAttribute("data-used", "true");
    // la fuente solo-recuperada NO genera sello en línea: se muestra en el panel de fuentes
    expect(screen.queryByTitle(/otro\.pdf/)).not.toBeInTheDocument();
    expect(screen.getByText(/Verificado/)).toBeInTheDocument();
    expect(screen.getByText(/Traza \(fijo\)/)).toBeInTheDocument();
  });

  it("muestra banner de verificación fallida y sugerencia de revisión", () => {
    const mensaje: MensajeChat = {
      id: "m2",
      rol: "assistant",
      contenido: "Respuesta dudosa (Fuente 1).",
      fuentes: [
        {
          indice: 1,
          documentoId: "d",
          documentoNombre: "a.pdf",
          chunkId: "c",
          chunkIndice: 0,
          pagina: 1,
          seccion: null,
          fragmento: "x",
          puntuacion: 0.5,
          usada: true,
        },
      ],
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
    const mensaje: MensajeChat = {
      id: "m3",
      rol: "assistant",
      contenido: "",
      pendiente: true,
    };
    render(<MessageBubble mensaje={mensaje} />);
    expect(screen.getByLabelText("generando respuesta")).toBeInTheDocument();
    expect(document.querySelectorAll(".citation-stamp")).toHaveLength(0);
  });
});
