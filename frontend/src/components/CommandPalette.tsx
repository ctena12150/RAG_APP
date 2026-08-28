import { useEffect, useMemo, useState } from "react";
import { Command } from "cmdk";

import { useApp } from "../state/AppContext";

/** Command palette (Ctrl/Cmd+K): navegación y búsqueda de conversaciones/documentos. */
export default function CommandPalette({ cerrar }: { cerrar: () => void }) {
  const { conversaciones, documentos, abrirConversacion, nuevaConversacion, setDominioActivo } = useApp();
  const [query, setQuery] = useState("");

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && cerrar();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [cerrar]);

  const convsFiltradas = useMemo(
    () =>
      query
        ? conversaciones.filter((c) => c.titulo.toLowerCase().includes(query.toLowerCase()))
        : conversaciones,
    [conversaciones, query],
  );
  const docsFiltrados = useMemo(
    () =>
      query ? documentos.filter((d) => d.nombreArchivo.toLowerCase().includes(query.toLowerCase())) : documentos,
    [documentos, query],
  );

  return (
    <div className="fixed inset-0 z-50 flex items-start justify-center pt-24" style={{ background: "rgba(2,6,14,.6)" }} onClick={cerrar}>
      <div
        className="w-[560px] max-w-[92vw] overflow-hidden rounded-xl"
        style={{ background: "var(--bg-elev)", border: "1px solid var(--line)" }}
        onClick={(e) => e.stopPropagation()}
        data-testid="command-palette"
      >
        <Command label="Paleta de comandos" className="text-sm">
          <Command.Input
            value={query}
            onValueChange={setQuery}
            autoFocus
            placeholder="Buscar conversaciones, documentos o acciones…"
            className="w-full px-4 py-3 outline-none"
            style={{ background: "transparent", color: "var(--ink)", borderBottom: "1px solid var(--line)" }}
          />
          <Command.List className="max-h-80 overflow-y-auto p-2">
            <Command.Empty className="py-4 text-center text-xs theme-ink-soft">Sin resultados.</Command.Empty>

            <Command.Group heading="Acciones" className="[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1 [&_[cmdk-group-heading]]:text-[10px] [&_[cmdk-group-heading]]:uppercase theme-ink-soft">
              <Item onSelect={() => { void nuevaConversacion(); cerrar(); }}>＋ Nueva conversación</Item>
              <Item onSelect={() => { setDominioActivo("rrhh"); cerrar(); }}>Ir a Recursos Humanos</Item>
              <Item onSelect={() => { setDominioActivo("mantenimiento"); cerrar(); }}>Ir a Mantenimiento</Item>
              <Item onSelect={() => { setDominioActivo("onboarding"); cerrar(); }}>Ir a Onboarding</Item>
            </Command.Group>

            {convsFiltradas.length > 0 && (
              <Command.Group heading="Conversaciones" className="[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1 [&_[cmdk-group-heading]]:text-[10px] [&_[cmdk-group-heading]]:uppercase theme-ink-soft">
                {convsFiltradas.slice(0, 8).map((c) => (
                  <Item key={c.id} onSelect={() => { void abrirConversacion(c.id); cerrar(); }}>
                    💬 {c.titulo}
                  </Item>
                ))}
              </Command.Group>
            )}

            {docsFiltrados.length > 0 && (
              <Command.Group heading="Documentos" className="[&_[cmdk-group-heading]]:px-2 [&_[cmdk-group-heading]]:py-1 [&_[cmdk-group-heading]]:text-[10px] [&_[cmdk-group-heading]]:uppercase theme-ink-soft">
                {docsFiltrados.slice(0, 8).map((d) => (
                  <Item key={d.id} onSelect={() => { setDominioActivo(d.dominio); cerrar(); }}>
                    📄 {d.nombreArchivo}
                  </Item>
                ))}
              </Command.Group>
            )}
          </Command.List>
        </Command>
      </div>
    </div>
  );
}

function Item({ children, onSelect }: { children: React.ReactNode; onSelect: () => void }) {
  return (
    <Command.Item
      onSelect={onSelect}
      className="cursor-pointer rounded-md px-3 py-2 text-sm data-[selected=true]:bg-white/5"
      style={{ color: "var(--ink)" }}
    >
      {children}
    </Command.Item>
  );
}
