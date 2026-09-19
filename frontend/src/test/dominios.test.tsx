import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import AdminUsuarios from "../components/AdminUsuarios";

vi.mock("../state/AppContext", () => ({ useApp: vi.fn() }));
import { useApp } from "../state/AppContext";

const CATALOGO = [
  { clave: "rrhh", etiqueta: "Recursos Humanos", descripcion: "Personal" },
  { clave: "it", etiqueta: "IT", descripcion: "Sistemas" },
];

function mockear(supero: Record<string, unknown> = {}) {
  vi.mocked(useApp).mockReturnValue({
    proveedoresDisponibles: [],
    dominios: CATALOGO,
    etiquetaDominio: (c: string) => CATALOGO.find((d) => d.clave === c)?.etiqueta ?? c,
    refrescarDominios: vi.fn().mockResolvedValue(undefined),
    listarDominios: vi.fn(),
    crearDominio: vi.fn(),
    actualizarDominio: vi.fn(),
    borrarDominio: vi.fn(),
    listarUsuarios: vi.fn().mockResolvedValue([]),
    crearUsuario: vi.fn(),
    actualizarUsuario: vi.fn(),
    borrarUsuario: vi.fn(),
    listarPermitidos: vi.fn().mockResolvedValue([]),
    crearPermitido: vi.fn(),
    actualizarPermitido: vi.fn(),
    borrarPermitido: vi.fn(),
    ...supero,
  } as unknown as ReturnType<typeof useApp>);
}

describe("AdminUsuarios · dominios", () => {
  it("lista los dominios del catálogo en su pestaña", async () => {
    mockear();
    render(<AdminUsuarios onClose={vi.fn()} />);
    fireEvent.click(screen.getByRole("tab", { name: "Dominios" }));
    expect(await screen.findByText("IT")).toBeInTheDocument();
  });

  it("el alta llama a crearDominio con clave, etiqueta y descripción", async () => {
    const crearDominio = vi.fn().mockResolvedValue({ clave: "legal", etiqueta: "Legal" });
    mockear({ crearDominio, refrescarDominios: vi.fn().mockResolvedValue(undefined) });
    render(<AdminUsuarios onClose={vi.fn()} />);
    fireEvent.click(screen.getByRole("tab", { name: "Dominios" }));
    fireEvent.click(screen.getByRole("button", { name: "+ Nuevo dominio" }));
    fireEvent.change(screen.getByLabelText("Clave"), { target: { value: "legal" } });
    fireEvent.change(screen.getByLabelText("Etiqueta"), { target: { value: "Legal" } });
    fireEvent.click(screen.getByRole("button", { name: "Crear dominio" }));
    expect(await vi.waitFor(() => crearDominio)).toHaveBeenCalledWith(
      expect.objectContaining({ clave: "legal", etiqueta: "Legal" }),
    );
  });
});
