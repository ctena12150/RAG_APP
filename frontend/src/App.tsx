import { useEffect, useState } from "react";
import { AppProvider, useApp } from "./state/AppContext";
import Landing from "./components/Landing";
import Sidebar from "./components/Sidebar";
import ChatPanel from "./components/ChatPanel";
import SourcesPanel from "./components/SourcesPanel";
import CommandPalette from "./components/CommandPalette";
import AdminUsuarios from "./components/AdminUsuarios";
import { ETIQUETA_ROL, type Rol } from "./lib/types";

function Shell() {
  const { vista, tema, alternarTema, usuario, cerrarSesion, esSuperUsuario } = useApp();
  const [paletteAbierta, setPaletteAbierta] = useState(false);
  const [adminAbierta, setAdminAbierta] = useState(false);
  const [drawerMovil, setDrawerMovil] = useState(false);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setPaletteAbierta((v) => !v);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  if (vista === "landing") return <Landing />;

  return (
    <div className="flex h-screen overflow-hidden" style={{ background: "var(--bg)", color: "var(--ink)" }}>
      {/* sidebar: drawer en móvil */}
      <button
        aria-label="Abrir menú"
        onClick={() => setDrawerMovil(true)}
        className="fixed bottom-4 left-4 z-40 rounded-full p-3 md:hidden"
        style={{ background: "var(--bg-elev)", border: "1px solid var(--line)" }}
      >
        ☰
      </button>
      <aside
        className={`${drawerMovil ? "translate-x-0" : "-translate-x-full"} fixed inset-y-0 left-0 z-30 w-72 transition-transform md:relative md:translate-x-0`}
        style={{ background: "var(--bg-elev)", borderRight: "1px solid var(--line)" }}
      >
        <Sidebar onNavegar={() => setDrawerMovil(false)} />
      </aside>
      {drawerMovil && (
        <div className="fixed inset-0 z-20 md:hidden" style={{ background: "var(--overlay)" }} onClick={() => setDrawerMovil(false)} />
      )}

      <main className="flex min-w-0 flex-1 flex-col">
        <header
          className="flex items-center justify-between px-5 py-2.5"
          style={{ borderBottom: "1px solid var(--line)" }}
        >
          <button
            onClick={() => setPaletteAbierta(true)}
            className="rounded-md px-3 py-1 text-xs"
            style={{ border: "1px solid var(--line)", color: "var(--ink-soft)" }}
          >
            Buscar… <kbd style={{ fontFamily: "var(--font-mono)" }}>Ctrl K</kbd>
          </button>
          <div className="flex items-center gap-3">
            {usuario && (
              <>
                <span
                  className="max-w-40 truncate text-xs"
                  style={{ color: "var(--ink-soft)" }}
                  title={usuario.email}
                >
                  {usuario.nombre ?? usuario.email}
                </span>
                {usuario.rol && (
                  <span
                    className="rounded-full border px-2 py-0.5 text-[10px] tracking-wide uppercase"
                    style={{ borderColor: "var(--line)", color: "var(--accent-a)" }}
                  >
                    {ETIQUETA_ROL[usuario.rol as Rol]}
                  </span>
                )}
                {esSuperUsuario && (
                  <button
                    onClick={() => setAdminAbierta(true)}
                    className="rounded-md px-2 py-1 text-xs cursor-pointer"
                    style={{ border: "1px solid var(--line)", color: "var(--accent-a)" }}
                  >
                    Usuarios
                  </button>
                )}
                <button
                  onClick={cerrarSesion}
                  className="rounded-md px-2 py-1 text-xs cursor-pointer"
                  style={{ border: "1px solid var(--line)" }}
                >
                  Salir
                </button>
              </>
            )}
            <span className="text-xs" style={{ color: "var(--ink-soft)" }}>
              {tema === "dark" ? "Noche" : "Día"}
            </span>
            <button
              onClick={alternarTema}
              aria-label="Alternar tema"
              className="rounded-md px-2 py-1 text-sm"
              style={{ border: "1px solid var(--line)" }}
            >
              {tema === "dark" ? "☀" : "☾"}
            </button>
          </div>
        </header>

        <div className="flex min-h-0 flex-1">
          <ChatPanel />
          <SourcesPanel />
        </div>
      </main>

      {paletteAbierta && <CommandPalette cerrar={() => setPaletteAbierta(false)} />}
      {adminAbierta && <AdminUsuarios onClose={() => setAdminAbierta(false)} />}
    </div>
  );
}

export default function App() {
  useEffect(() => {
    document.documentElement.dataset.view = "app";
  }, []);
  // la landing fija data-view="landing" al montarse; el shell restaura "app"
  return (
    <AppProvider>
      <VistaConTema />
    </AppProvider>
  );
}

function VistaConTema() {
  const { vista } = useApp();
  useEffect(() => {
    document.documentElement.dataset.view = vista;
  }, [vista]);
  return vista === "landing" ? <Landing /> : <Shell />;
}
