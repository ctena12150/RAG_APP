import { describe, expect, it } from "vitest";
import { dividirPorCitas, normalizarMensaje } from "../lib/api";
import { exportarConversacionMarkdown } from "../lib/export";
import type { Conversacion, MensajeChat } from "../lib/types";

describe("dividirPorCitas", () => {
  it("separa el texto y las referencias (Fuente N)", () => {
    const partes = dividirPorCitas("Hay 23 días (Fuente 1). La nómina el día 25 (Fuente 2).");
    expect(partes.filter((p) => p.tipo === "cita").map((p) => p.valor)).toEqual(["1", "2"]);
    expect(partes.filter((p) => p.tipo === "texto")).toHaveLength(3);
  });

  it("devuelve el texto intacto sin citas", () => {
    const partes = dividirPorCitas("Respuesta sin fuentes.");
    expect(partes).toHaveLength(1);
    expect(partes[0].tipo).toBe("texto");
  });

  it("ignora citas con índices fuera de rango al listarlas (el filtro de fuente es del componente)", () => {
    const partes = dividirPorCitas("X (Fuente 99)");
    expect(partes.some((p) => p.tipo === "cita" && p.valor === "99")).toBe(true);
  });
});

describe("normalizarMensaje", () => {
  it("convierte los campos JSON persistidos por .NET al contrato del frontend", () => {
    const mensaje = normalizarMensaje({
      id: "m1",
      rol: "assistant",
      contenido: "Respuesta (Fuente 1).",
      fuentesJson: JSON.stringify([{ indice: 1, documentoId: "d1", documentoNombre: "manual.pdf", chunkId: "c1", chunkIndice: 0, fragmento: "texto", puntuacion: 0.9, usada: true }]),
      trazaJson: JSON.stringify({ modo: "fijo", etapas: [{ etapa: "busqueda", duracionMs: 10 }] }),
      verificacionJson: JSON.stringify({ verdict: "supported" }),
    });

    expect(mensaje.fuentes).toHaveLength(1);
    expect(mensaje.fuentes?.[0].documentoNombre).toBe("manual.pdf");
    expect(mensaje.traza?.modo).toBe("fijo");
    expect(mensaje.verificacion?.verdict).toBe("supported");
  });
});

describe("exportarConversacionMarkdown", () => {
  const conversacion: Conversacion = {
    id: "c1",
    titulo: "Vacaciones",
    tituloAutomatico: true,
    dominios: ["rrhh"],
    creadoUtc: new Date().toISOString(),
  };

  const mensajes: MensajeChat[] = [
    { id: "m1", rol: "user", contenido: "¿Cuántos días de vacaciones?" },
    {
      id: "m2",
      rol: "assistant",
      contenido: "23 días (Fuente 1).",
      fuentes: [
        {
          indice: 1,
          documentoId: "d",
          documentoNombre: "politica.pdf",
          chunkId: "ch",
          chunkIndice: 0,
          pagina: 3,
          seccion: null,
          fragmento: "Son 23 días…",
          puntuacion: 0.9,
          usada: true,
        },
      ],
      verificacion: { verdict: "supported" },
    },
  ];

  it("incluye pregunta, respuesta, fuentes y nota de verificación", () => {
    const md = exportarConversacionMarkdown(conversacion, mensajes);
    expect(md).toContain("# Vacaciones");
    expect(md).toContain("## Pregunta");
    expect(md).toContain("¿Cuántos días de vacaciones?");
    expect(md).toContain("**politica.pdf**, p. 3 — _✓ citada_");
    expect(md).toContain("Verificación: respuesta sostenida por las fuentes");
  });
});
