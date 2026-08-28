import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AppProvider, useApp } from "../state/AppContext";
import type { Documento, Dominio } from "../lib/types";
import { api } from "../lib/api";

vi.mock("../lib/api", () => ({
  api: {
    subirDocumento: vi.fn(),
    estadoDocumento: vi.fn(),
    listarDocumentos: vi.fn(async () => []),
    listarFolders: vi.fn(async () => []),
    listarConversaciones: vi.fn(async () => []),
  },
  streamChat: vi.fn(),
  describirAgente: vi.fn(() => ""),
}));

const documentoFalso: Documento = {
  id: "d1",
  nombreArchivo: "manual.pdf",
  dominio: "rrhh" as Dominio,
  folderId: null,
  tamanoBytes: 10,
  estado: "pendiente",
  errorMensaje: null,
  totalPaginas: null,
  creadoUtc: new Date().toISOString(),
};

let resolverSubida: ((doc: Documento) => void) | null = null;

function Sonda() {
  const { subidaActiva, docResaltado, errorSubida, subirDocumento } = useApp();
  return (
    <div>
      <button onClick={() => void subirDocumento(new File(["contenido"], "manual.pdf"), "rrhh")}>
        subir
      </button>
      <div data-testid="fase">
        {subidaActiva ? `${subidaActiva.fase}:${Math.round(subidaActiva.progresoSubida * 100)}` : "inactiva"}
      </div>
      <div data-testid="resaltado">{docResaltado ? `${docResaltado.id}:${docResaltado.estado}` : ""}</div>
      <div data-testid="error-subida">{errorSubida ?? ""}</div>
    </div>
  );
}

describe("subida con progreso", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    resolverSubida = null;
    vi.mocked(api.subirDocumento).mockReset();
    vi.mocked(api.estadoDocumento).mockReset();
    vi.mocked(api.listarDocumentos).mockResolvedValue([]);
    vi.mocked(api.listarFolders).mockResolvedValue([]);
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  it("recorre subiendo → procesando → listo y resalta la fila unos segundos", async () => {
    vi.mocked(api.subirDocumento).mockImplementation((_f, _d, ops) => {
      ops?.onProgresoSubida?.(0.5);
      return new Promise<Documento>((resolve) => {
        resolverSubida = resolve;
      });
    });
    vi.mocked(api.estadoDocumento).mockResolvedValue({ estado: "procesando" });

    render(
      <AppProvider>
        <Sonda />
      </AppProvider>,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "subir" }));
    });
    expect(screen.getByTestId("fase")).toHaveTextContent("subiendo:50");

    await act(async () => {
      resolverSubida?.(documentoFalso);
      await Promise.resolve();
    });
    expect(screen.getByTestId("fase")).toHaveTextContent("procesando:100");

    // primer tick del polling: sigue procesando, nada resaltado aún
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1500);
    });
    expect(screen.getByTestId("resaltado")).toHaveTextContent("");
    expect(api.estadoDocumento).toHaveBeenCalledWith("d1");

    // el backend termina: resaltado + barra fuera
    vi.mocked(api.estadoDocumento).mockResolvedValue({ estado: "listo" });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1500);
    });
    expect(screen.getByTestId("fase")).toHaveTextContent("inactiva");
    expect(screen.getByTestId("resaltado")).toHaveTextContent("d1:listo");

    // autolimpieza del destello tras ~6s
    await act(async () => {
      await vi.advanceTimersByTimeAsync(6500);
    });
    expect(screen.getByTestId("resaltado")).toHaveTextContent("");
  });

  it("muestra errorSubida y limpia la barra si el upload falla", async () => {
    vi.mocked(api.subirDocumento).mockRejectedValue(new Error("Formato de archivo no soportado"));

    render(
      <AppProvider>
        <Sonda />
      </AppProvider>,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "subir" }));
    });

    expect(screen.getByTestId("error-subida")).toHaveTextContent("Formato de archivo no soportado");
    expect(screen.getByTestId("fase")).toHaveTextContent("inactiva");
    expect(screen.getByTestId("resaltado")).toHaveTextContent("");
  });

  it("limpia la barra al agotar el tope de procesamiento sin dejar resaltado falso", async () => {
    vi.mocked(api.subirDocumento).mockResolvedValue(documentoFalso);
    vi.mocked(api.estadoDocumento).mockResolvedValue({ estado: "procesando" });

    render(
      <AppProvider>
        <Sonda />
      </AppProvider>,
    );

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "subir" }));
    });
    expect(screen.getByTestId("fase")).toHaveTextContent("procesando:100");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(600_000);
    });
    expect(screen.getByTestId("fase")).toHaveTextContent("inactiva");
    expect(screen.getByTestId("resaltado")).toHaveTextContent("");

    // el polling se detuvo: no vuelve a consultar ni a marcar nada
    const llamadas = vi.mocked(api.estadoDocumento).mock.calls.length;
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });
    expect(vi.mocked(api.estadoDocumento).mock.calls.length).toBe(llamadas);
  });
});
