import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import Landing from "../components/Landing";

vi.mock("../state/AppContext", () => ({ useApp: vi.fn() }));
// la landing es pesada: se sustituyen los decorativos por componentes vacíos
vi.mock("../components/LogoMark", () => ({ LogoMark: () => <span /> }));
vi.mock("../components/AmbientBackground", () => ({ AmbientBackground: () => null }));
vi.mock("../components/GrainOverlay", () => ({ GrainOverlay: () => null }));
vi.mock("../components/hero/SignalBackground", () => ({ default: () => null }));
// Login re-exporta useApp; provee API vacía para no tocar fetch real
vi.mock("../lib/api", () => ({
  api: {},
  describirAgente: vi.fn(() => ""),
  streamChat: vi.fn(),
}));

import { useApp } from "../state/AppContext";

const useAppMock = vi.mocked(useApp);

function baseApp(sobre: Record<string, unknown>) {
  return {
    entrarApp: vi.fn(),
    tema: "dark",
    alternarTema: vi.fn(),
    usuario: null,
    proveedoresDisponibles: [] as string[],
    authCargando: false,
    iniciarSesion: vi.fn(),
    iniciarSesionLocal: vi.fn(),
    cerrarSesion: vi.fn(),
    ...sobre,
  } as unknown as ReturnType<typeof useApp>;
}

describe("Landing · puerta de acceso al chat", () => {
  it("con auth configurada y sin sesión muestra el CTA público (login tras pulsarlo)", () => {
    useAppMock.mockReturnValue(baseApp({ proveedoresDisponibles: ["google"] }));

    render(<Landing />);

    expect(screen.getByRole("button", { name: /Empezar a consultar/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Continuar con Google/ })).not.toBeInTheDocument();
  });

  it("pulsar el CTA con auth abre el modal de login", () => {
    useAppMock.mockReturnValue(baseApp({ proveedoresDisponibles: ["google"] }));

    render(<Landing />);

    fireEvent.click(screen.getByRole("button", { name: /Empezar a consultar/ }));

    expect(screen.getByRole("dialog", { name: "Iniciar sesión" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Continuar con Google/ })).toBeInTheDocument();
  });

  it("el modal se cierra con el botón Cerrar y con Escape", () => {
    useAppMock.mockReturnValue(baseApp({ proveedoresDisponibles: ["google"] }));

    const { rerender } = render(<Landing />);

    fireEvent.click(screen.getByRole("button", { name: /Empezar a consultar/ }));
    expect(screen.getByRole("dialog", { name: "Iniciar sesión" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Cerrar" }));
    expect(screen.queryByRole("dialog", { name: "Iniciar sesión" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Empezar a consultar/ }));
    fireEvent.keyDown(window, { key: "Escape" });
    expect(screen.queryByRole("dialog", { name: "Iniciar sesión" })).not.toBeInTheDocument();
    rerender(<></>);
  });

  it("pulsar el CTA con login local revela el formulario de usuario y contraseña", () => {
    useAppMock.mockReturnValue(baseApp({ proveedoresDisponibles: ["google", "local"] }));

    render(<Landing />);

    fireEvent.click(screen.getByRole("button", { name: /Empezar a consultar/ }));

    expect(screen.getByLabelText("Usuario")).toBeInTheDocument();
    expect(screen.getByLabelText("Contraseña")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Continuar con Google/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Microsoft/ })).not.toBeInTheDocument();
  });

  it("enviar el formulario local llama a iniciarSesionLocal", async () => {
    const iniciarSesionLocal = vi.fn().mockResolvedValue(undefined);
    useAppMock.mockReturnValue(baseApp({ proveedoresDisponibles: ["local"], iniciarSesionLocal }));

    render(<Landing />);

    fireEvent.click(screen.getByRole("button", { name: /Empezar a consultar/ }));
    fireEvent.change(screen.getByLabelText("Usuario"), { target: { value: "carla" } });
    fireEvent.change(screen.getByLabelText("Contraseña"), { target: { value: "ClaveTest123!" } });
    fireEvent.click(screen.getByRole("button", { name: "Entrar" }));

    expect(iniciarSesionLocal).toHaveBeenCalledWith("carla", "ClaveTest123!");
  });

  it("pulsar el CTA con sesión entra directo sin mostrar login", () => {
    const entrarApp = vi.fn();
    useAppMock.mockReturnValue(
      baseApp({
        entrarApp,
        usuario: { email: "carla@empresa.com", proveedor: "google" },
        proveedoresDisponibles: ["google"],
      }),
    );

    render(<Landing />);

    fireEvent.click(screen.getByRole("button", { name: /Empezar a consultar/ }));

    expect(entrarApp).toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: /Continuar con Google/ })).not.toBeInTheDocument();
  });

  it("con sesión válida muestra el CTA de entrar al chat", () => {
    useAppMock.mockReturnValue(
      baseApp({
        usuario: { email: "carla@empresa.com", proveedor: "google" },
        proveedoresDisponibles: ["google"],
      }),
    );

    render(<Landing />);

    expect(screen.getByRole("button", { name: /Empezar a consultar/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Continuar con Google/ })).not.toBeInTheDocument();
  });

  it("sin proveedores configurados (auth desactivada) permite entrar directamente", () => {
    useAppMock.mockReturnValue(baseApp({ proveedoresDisponibles: [] }));

    render(<Landing />);

    expect(screen.getByRole("button", { name: /Empezar a consultar/ })).toBeInTheDocument();
  });
});