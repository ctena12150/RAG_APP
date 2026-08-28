import { useEffect, useState } from "react";
import { useApp } from "../state/AppContext";
import type { Fuente, TrazaPipeline } from "../lib/types";

/** Panel derecho técnico: la fuente de cada respuesta vive en el chat. */
export default function SourcesPanel() {
  const { chat } = useApp();
  const [tab, setTab] = useState<"traza">("traza");
  

  useEffect(() => {
    const onFuente = (e: Event) => {
      const detail = (e as CustomEvent).detail as { chunkId?: string; fuente?: Fuente };
      if (!detail?.fuente) return;
    };
    const onTraza = () => {
      setTab("traza");
    };
    window.addEventListener("fuente-seleccionada", onFuente);
    window.addEventListener("mostrar-traza", onTraza);
    return () => {
      window.removeEventListener("fuente-seleccionada", onFuente);
      window.removeEventListener("mostrar-traza", onTraza);
    };
    }, [chat.mensajes]);

  const ultimoAsistenteConFuentes = [...chat.mensajes]
    .reverse()
    .find((m) => m.rol === "assistant" && m.fuentes && !m.pendiente);

  const traza: TrazaPipeline | null | undefined = ultimoAsistenteConFuentes?.traza;

  return (
    <aside
      className="hidden w-80 shrink-0 flex-col overflow-hidden md:flex"
      style={{ borderLeft: "1px solid var(--line)", background: "var(--bg-elev)" }}
      data-testid="panel-fuentes"
    >
      <div className="flex" style={{ borderBottom: "1px solid var(--line)" }}>
        <button
          className="w-full py-2 text-xs"
          style={{
            color: tab === "traza" ? "var(--accent-a)" : "var(--ink-soft)",
            borderBottom: `2px solid ${tab === "traza" ? "var(--accent-a)" : "transparent"}`,
          }}
          onClick={() => setTab("traza")}
        >
          Traza del pipeline
        </button>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto p-3">
        {tab === "traza" && traza ? (
          <InspectorTraza traza={traza} />
        ) : (
          <p className="mt-8 text-center text-xs theme-ink-soft">
            La traza técnica aparecerá después de una respuesta.
          </p>
        )}
      </div>
    </aside>
  );
}

function InspectorTraza({ traza }: { traza: TrazaPipeline }) {
  const maxDuracion = Math.max(...traza.etapas.map((e) => e.duracionMs), 1);

  return (
    <ol className="relative space-y-4 pl-4" data-testid="inspector-traza">
      <span className="absolute bottom-2 left-[5px] top-2 w-px" style={{ background: "var(--line)" }} />
      {traza.etapas.map((e, i) => (
        <li key={i} className="relative">
          <span
            className="absolute -left-4 top-1 h-2.5 w-2.5 rounded-full"
            style={{ background: i === traza.etapas.length - 1 ? "var(--accent-b)" : "var(--accent-a)" }}
          />
          <div className="flex items-baseline justify-between gap-2">
            <span className="text-xs font-semibold capitalize">{e.etapa.replace(/_/g, " ")}</span>
            <span className="text-[10px]" style={{ fontFamily: "var(--font-mono)", color: "var(--ink-soft)" }}>
              {e.duracionMs} ms
            </span>
          </div>
          <div className="mt-1 h-1 rounded-full" style={{ background: "var(--line)" }}>
            <div
              className="h-1 rounded-full"
              style={{
                width: `${Math.max((e.duracionMs / maxDuracion) * 100, 3)}%`,
                background: "var(--accent-a)",
              }}
            />
          </div>
          {e.detalle && Object.keys(e.detalle).length > 0 && (
            <pre
              className="mt-1 overflow-x-auto rounded p-1.5 text-[10px]"
              style={{ background: "var(--bg)", fontFamily: "var(--font-mono)", color: "var(--ink-soft)" }}
            >
              {JSON.stringify(e.detalle, null, 1).slice(0, 400)}
            </pre>
          )}
        </li>
      ))}
      {traza.etapas.length === 0 && <p className="text-xs theme-ink-soft">Sin etapas registradas.</p>}
    </ol>
  );
}
