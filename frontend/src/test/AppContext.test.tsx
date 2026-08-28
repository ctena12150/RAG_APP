import { act, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AppProvider, useApp } from "../state/AppContext";

const { apiMock, streamChatMock } = vi.hoisted(() => ({
  apiMock: {
    crearConversacion: vi.fn(),
    mensajesDe: vi.fn(),
  },
  streamChatMock: vi.fn(),
}));

vi.mock("../lib/api", () => ({
  api: apiMock,
  describirAgente: vi.fn(() => ""),
  streamChat: streamChatMock,
}));

function TestChat() {
  const { chat, preguntar } = useApp();
  return (
    <>
      <button onClick={() => void preguntar("¿Qué documentos hay?")}>preguntar</button>
      <output>{chat.error}</output>
      <span data-testid="cantidad-mensajes">{chat.mensajes.length}</span>
      <div data-testid="mensajes">{chat.mensajes.map((mensaje) => mensaje.contenido).join("|")}</div>
    </>
  );
}

describe("AppContext: errores del stream", () => {
  it("conserva el error y el estado local tras finalizar el stream", async () => {
    apiMock.crearConversacion.mockResolvedValue({
      id: "conv-1",
      titulo: "Nueva conversación",
      tituloAutomatico: true,
      dominios: [],
      creadoUtc: "2026-01-01T00:00:00Z",
    });
    apiMock.mensajesDe.mockResolvedValue([]);
    streamChatMock.mockImplementation(async (_path, _body, handlers) => {
      handlers.onError({ code: "proveedor_indisponible", message: "El proveedor no está disponible." });
    });

    render(
      <AppProvider>
        <TestChat />
      </AppProvider>,
    );

    await act(async () => {
      screen.getByRole("button", { name: "preguntar" }).click();
    });

    expect(screen.getByText("El proveedor no está disponible.")).toBeInTheDocument();
    expect(screen.getByTestId("cantidad-mensajes")).toHaveTextContent("1");
    expect(apiMock.mensajesDe).not.toHaveBeenCalled();
  });
});
