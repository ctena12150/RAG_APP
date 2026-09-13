import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import Sidebar from "../components/Sidebar";
import AdminUsuarios from "../components/AdminUsuarios";
import type { UsuarioAdmin } from "../lib/types";

vi.mock("../state/AppContext", () => ({ useApp: vi.fn() }));
import { useApp } from "../state/AppContext";

const useAppMock = vi.mocked(useApp);

function baseSidebarApp(sobre: Record<string, unknown>) {
  return {
    dominioActivo: "rrhh",
    setDominioActivo: vi.fn(),
    documentos: [],
    folders: [],
    crearFolder: vi.fn(),
    borrarFolder: vi.fn(),
    subirDocumento: vi.fn(),
    subidaActiva: null,
    errorSubida: null,
    conversaciones: [],
    conversacionActiva: null,
    abrirConversacion: vi.fn(),
    nuevaConversacion: vi.fn(),
    borrarConversacion: vi.fn(),
    puedeGestionarDominio: () => true,
    ...sobre,
  } as unknown as ReturnType<typeof useApp>;
}

describe("Sidebar · gating por rol", () => {
  it("el usuario (rol usuario) no ve la pestaña de documentos", () => {
    useAppMock.mockReturnValue(baseSidebarApp({ puedeGestionarDocumentos: false }));

    render(<Sidebar />);

    expect(screen.getByRole("button", { name: "conversaciones" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "documentos" })).not.toBeInTheDocument();
  });

  it("el teamleader ve la pestaña de documentos además del chat", () => {
    useAppMock.mockReturnValue(baseSidebarApp({ puedeGestionarDocumentos: true }));

    render(<Sidebar />);

    expect(screen.getByRole("button", { name: "conversaciones" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "documentos" })).toBeInTheDocument();
  });
});

describe("Sidebar · teamleader acotado a sus dominios", () => {
  it("solo muestra sus dominios y oculta la pill Todas", () => {
    useAppMock.mockReturnValue(
      baseSidebarApp({ puedeGestionarDocumentos: true, dominiosGestionables: ["rrhh"] }),
    );
    render(<Sidebar />);
    fireEvent.click(screen.getByRole("button", { name: "documentos" }));

    expect(screen.getByRole("button", { name: "Recursos Humanos" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Mantenimiento" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Onboarding" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Todas" })).not.toBeInTheDocument();
  });

  it("el superusuario (null) ve todos los dominios y Todas", () => {
    useAppMock.mockReturnValue(
      baseSidebarApp({ puedeGestionarDocumentos: true, dominiosGestionables: null }),
    );
    render(<Sidebar />);
    fireEvent.click(screen.getByRole("button", { name: "documentos" }));

    expect(screen.getByRole("button", { name: "Recursos Humanos" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Mantenimiento" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Todas" })).toBeInTheDocument();
  });
});

describe("Sidebar · gating por dominio", () => {
  function appDocumentos(sobre: Record<string, unknown>) {
    return baseSidebarApp({
      puedeGestionarDocumentos: true,
      documentos: [
        {
          id: "d1",
          nombreArchivo: "guia.pdf",
          dominio: "rrhh",
          folderId: null,
          tamanoBytes: 10,
          estado: "listo",
          creadoUtc: "2026-01-01T00:00:00Z",
        },
      ],
      ...sobre,
    });
  }

  it("habilita la subida cuando el dominio activo es gestionable", () => {
    useAppMock.mockReturnValue(appDocumentos({ puedeGestionarDominio: (d: string) => d === "rrhh" }));
    render(<Sidebar />);
    fireEvent.click(screen.getByRole("button", { name: "documentos" }));

    expect(screen.getByRole("button", { name: "↑ Subir documento" })).toBeEnabled();
  });

  it("deshabilita la subida cuando el dominio activo no es gestionable", () => {
    useAppMock.mockReturnValue(
      appDocumentos({ dominioActivo: "mantenimiento", puedeGestionarDominio: (d: string) => d === "rrhh" }),
    );
    render(<Sidebar />);
    fireEvent.click(screen.getByRole("button", { name: "documentos" }));

    expect(screen.getByRole("button", { name: "↑ Subir documento" })).toBeDisabled();
  });
});

describe("AdminUsuarios · panel del superusuario", () => {
  const luis: UsuarioAdmin = {
    id: "u2",
    usuario: "luis",
    nombre: "Luis Moreno",
    rol: "teamleader" as const,
    dominios: ["rrhh"],
    activo: true,
    creadoUtc: "2026-01-01T00:00:00Z",
  };

  function renderPanel(supero: Partial<ReturnType<typeof useApp>>) {
    vi.mocked(useApp).mockReturnValue({
      proveedoresDisponibles: [],
      listarUsuarios: vi.fn().mockResolvedValue([luis]),
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

  it("lista las cuentas locales al abrir", async () => {
    renderPanel({});

    render(<AdminUsuarios onClose={vi.fn()} />);

    expect(await screen.findByText("luis")).toBeInTheDocument();
    expect(screen.getByText("Luis Moreno")).toBeInTheDocument();
  });

  it("muestra los dominios del teamleader", async () => {
    renderPanel({});

    render(<AdminUsuarios onClose={vi.fn()} />);

    expect(await screen.findByText("Recursos Humanos")).toBeInTheDocument();
  });

  it("el buscador filtra por nombre", async () => {
    renderPanel({});

    render(<AdminUsuarios onClose={vi.fn()} />);
    await screen.findByText("luis");

    fireEvent.change(screen.getByLabelText(/Buscar por usuario/), { target: { value: "nadie" } });
    expect(screen.queryByText("luis")).not.toBeInTheDocument();
    expect(screen.getByText("Sin resultados para esta búsqueda.")).toBeInTheDocument();
  });

  it("desactivar una cuenta llama a actualizarUsuario", async () => {
    const actualizarUsuario = vi.fn().mockImplementation(async (_id: string, cambios: { activo?: boolean }) => ({
      ...luis,
      activo: cambios.activo ?? luis.activo,
    }));
    renderPanel({ actualizarUsuario });

    render(<AdminUsuarios onClose={vi.fn()} />);
    await screen.findByText("luis");

    fireEvent.click(screen.getByLabelText("Activo luis"));

    expect(actualizarUsuario).toHaveBeenCalledWith("u2", { activo: false });
  });

  it("editar en diálogo guarda nombre, email, rol y dominios", async () => {
    const actualizarUsuario = vi.fn().mockResolvedValue({ ...luis, nombre: "Luis M." });
    renderPanel({ actualizarUsuario });

    render(<AdminUsuarios onClose={vi.fn()} />);
    await screen.findByText("luis");

    fireEvent.click(screen.getByRole("button", { name: "Editar luis" }));
    fireEvent.change(screen.getByLabelText("Nombre"), { target: { value: "Luis M." } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar" }));

    expect(actualizarUsuario).toHaveBeenCalledWith(
      "u2",
      expect.objectContaining({ nombre: "Luis M.", rol: "teamleader", dominios: ["rrhh"] }),
    );
  });

  it("eliminar pide confirmación y borra", async () => {
    const borrarUsuario = vi.fn().mockResolvedValue(undefined);
    renderPanel({ borrarUsuario });

    render(<AdminUsuarios onClose={vi.fn()} />);
    await screen.findByText("luis");

    fireEvent.click(screen.getByRole("button", { name: "Eliminar luis" }));
    expect(screen.getByText(/¿Eliminar a luis\?/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Eliminar" }));
    expect(borrarUsuario).toHaveBeenCalledWith("u2");
  });

  it("el alta en diálogo crea el usuario con su rol, dominios y aparece en la lista", async () => {
    const crearUsuario = vi.fn().mockResolvedValue({
      id: "u3",
      usuario: "nuevo",
      nombre: null,
      email: "nuevo@empresa.com",
      rol: "teamleader",
      dominios: ["rrhh", "mantenimiento", "onboarding"],
      activo: true,
      creadoUtc: "2026-01-01T00:00:00Z",
    });
    renderPanel({ crearUsuario });

    render(<AdminUsuarios onClose={vi.fn()} />);
    await screen.findByText("luis");

    fireEvent.click(screen.getByRole("button", { name: "+ Nuevo usuario" }));
    fireEvent.change(screen.getByLabelText("Usuario"), { target: { value: "nuevo" } });
    fireEvent.change(screen.getByLabelText("Nombre"), { target: { value: "Nuevo Usuario" } });
    fireEvent.change(screen.getByLabelText("Email"), { target: { value: "nuevo@empresa.com" } });
    fireEvent.change(screen.getByLabelText("Rol"), { target: { value: "teamleader" } });
    fireEvent.change(screen.getByLabelText("Contraseña (mín. 8 caracteres)"), {
      target: { value: "ClaveTest123!" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Crear usuario" }));

    expect(crearUsuario).toHaveBeenCalledWith({
      usuario: "nuevo",
      contrasena: "ClaveTest123!",
      rol: "teamleader",
      email: "nuevo@empresa.com",
      nombre: "Nuevo Usuario",
      dominios: ["rrhh", "mantenimiento", "onboarding"],
    });
    expect(await screen.findByText("nuevo")).toBeInTheDocument();
  });
});

describe("AdminUsuarios · lista blanca Google", () => {
  function renderBlanca(supero: Partial<ReturnType<typeof useApp>>) {
    vi.mocked(useApp).mockReturnValue({
      proveedoresDisponibles: ["google", "local"],
      listarUsuarios: vi.fn().mockResolvedValue([]),
      crearUsuario: vi.fn(),
      actualizarUsuario: vi.fn(),
      borrarUsuario: vi.fn(),
      listarPermitidos: vi.fn().mockResolvedValue([
        { id: "p1", email: "carla@empresa.com", dominio: null, activo: true, creadoUtc: "2026-01-01T00:00:00Z" },
      ]),
      crearPermitido: vi.fn(),
      actualizarPermitido: vi.fn(),
      borrarPermitido: vi.fn(),
      ...supero,
    } as unknown as ReturnType<typeof useApp>);
  }

  it("muestra la pestaña solo con Google y lista las entradas", async () => {
    renderBlanca({});

    render(<AdminUsuarios onClose={vi.fn()} />);

    fireEvent.click(screen.getByRole("tab", { name: "Lista blanca Google" }));
    expect(await screen.findByText("carla@empresa.com")).toBeInTheDocument();
  });

  it("el alta de dominio llama a crearPermitido", async () => {
    const crearPermitido = vi.fn().mockResolvedValue({
      id: "p2",
      email: null,
      dominio: "socios.com",
      activo: true,
      creadoUtc: "2026-01-01T00:00:00Z",
    });
    renderBlanca({ crearPermitido });

    render(<AdminUsuarios onClose={vi.fn()} />);
    fireEvent.click(screen.getByRole("tab", { name: "Lista blanca Google" }));
    await screen.findByText("carla@empresa.com");

    fireEvent.click(screen.getByRole("button", { name: "+ Nueva entrada" }));
    fireEvent.click(screen.getByLabelText("Dominio completo"));
    fireEvent.change(screen.getByLabelText("Dominio (p. ej. empresa.com)"), { target: { value: "socios.com" } });
    fireEvent.click(screen.getByRole("button", { name: "Añadir" }));

    expect(crearPermitido).toHaveBeenCalledWith({ email: null, dominio: "socios.com" });
    expect(await screen.findByText("socios.com")).toBeInTheDocument();
  });

  it("desactivar y quitar una entrada llaman a la API", async () => {
    const actualizarPermitido = vi.fn().mockResolvedValue({
      id: "p1",
      email: "carla@empresa.com",
      dominio: null,
      activo: false,
      creadoUtc: "2026-01-01T00:00:00Z",
    });
    const borrarPermitido = vi.fn().mockResolvedValue(undefined);
    renderBlanca({ actualizarPermitido, borrarPermitido });

    render(<AdminUsuarios onClose={vi.fn()} />);
    fireEvent.click(screen.getByRole("tab", { name: "Lista blanca Google" }));
    await screen.findByText("carla@empresa.com");

    fireEvent.click(screen.getByLabelText("Activa carla@empresa.com"));
    expect(actualizarPermitido).toHaveBeenCalledWith("p1", { activo: false });

    fireEvent.click(screen.getByRole("button", { name: "Eliminar carla@empresa.com" }));
    fireEvent.click(screen.getByRole("button", { name: "Quitar" }));
    expect(borrarPermitido).toHaveBeenCalledWith("p1");
  });
});
