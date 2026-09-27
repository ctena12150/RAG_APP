import { Suspense, lazy, useEffect, useState } from "react";
import { X } from "lucide-react";
import { api } from "../lib/api";
import type { EstadisticasResumen } from "../lib/types";

const Graficos = lazy(() => import("./GraficosEstadisticas"));

type Periodo = "7" | "30" | "todo";

function desdeDe(periodo: Periodo): string | undefined {
  if (periodo === "todo") return undefined;
  const dias = periodo === "7" ? 7 : 30;
  return new Date(Date.now() - dias * 864e5).toISOString();
}

export default function PanelEstadisticas({ onClose }: { onClose: () => void }) {
  const [periodo, setPeriodo] = useState<Periodo>("30");
  const [datos, setDatos] = useState<EstadisticasResumen | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(true);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  useEffect(() => {
    let activo = true;
    setCargando(true);
    setError(null);
    api
      .estadisticas(desdeDe(periodo))
      .then((r) => {
        if (activo) {
          setDatos(r);
          setCargando(false);
        }
      })
      .catch((err) => {
        if (activo) {
          setError(err instanceof Error ? err.message : "No se pudieron cargar las estadísticas.");
          setCargando(false);
        }
      });
    return () => {
      activo = false;
    };
  }, [periodo]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ background: "var(--overlay)" }}
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Estadísticas de consultas"
        className="flex max-h-[88vh] w-[760px] max-w-[96vw] flex-col rounded-2xl"
        style={{ background: "var(--bg-elev)", border: "1px solid var(--line)", color: "var(--ink)" }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-4 border-b px-6 py-4" style={{ borderColor: "var(--line)" }}>
          <div>
            <h2 className="text-lg font-semibold leading-tight">Lo que pregunta tu equipo</h2>
            <p className="text-xs" style={{ color: "var(--ink-soft)" }}>
              Agregado global · sin identidad de quién preguntó
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Cerrar"
            className="flex h-8 w-8 items-center justify-center rounded-full border cursor-pointer"
            style={{ borderColor: "var(--line)", color: "var(--ink-soft)" }}
          >
            <X size={15} />
          </button>
        </div>

        <div className="flex gap-1.5 px-6 pt-3" role="group" aria-label="Periodo">
          {(["7", "30", "todo"] as Periodo[]).map((p) => (
            <button
              key={p}
              type="button"
              onClick={() => setPeriodo(p)}
              aria-pressed={periodo === p}
              className="rounded-full px-3 py-1 text-xs cursor-pointer"
              style={{
                border: `1px solid ${periodo === p ? "var(--accent-a)" : "var(--line)"}`,
                color: periodo === p ? "var(--accent-a)" : "var(--ink-soft)",
              }}
            >
              {p === "todo" ? "Todo" : `${p} días`}
            </button>
          ))}
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-6 py-4">
          {cargando && (
            <p className="py-10 text-center text-sm" style={{ color: "var(--ink-soft)" }}>
              Cargando estadísticas…
            </p>
          )}
          {error && (
            <p className="py-6 text-center text-sm" role="alert" style={{ color: "var(--accent-b)" }}>
              {error}
            </p>
          )}
          {!cargando && !error && datos && (
            <>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                <Tile numero={String(datos.totalConsultas)} etiqueta={datos.totalConsultas === 1 ? "consulta" : "consultas"} />
                <Tile numero={`${datos.pctResueltas} %`} etiqueta="resueltas con el manual" sub={`${datos.sirvieron} de ${datos.totalConsultas}`} />
                <Tile numero={String(datos.sinCobertura)} etiqueta="no estaban en el manual" />
                <Tile numero={String(datos.votosMal)} etiqueta="👎 no sirvieron" sub="el manual traía algo, pero no ayudó" />
              </div>
              {datos.tiempoMedioMs != null && (
                <p className="mt-2 text-[11px]" style={{ color: "var(--ink-soft)" }}>
                  Tiempo medio de generación: {(datos.tiempoMedioMs / 1000).toFixed(1)} s
                </p>
              )}
              <Suspense
                fallback={
                  <p className="py-4 text-center text-xs" style={{ color: "var(--ink-soft)" }}>
                    Cargando gráficos…
                  </p>
                }
              >
                <Graficos datos={datos} />
              </Suspense>
              <Listas datos={datos} />
            </>
          )}
          {!cargando && !error && datos && datos.totalConsultas === 0 && (
            <p className="py-6 text-center text-sm" style={{ color: "var(--ink-soft)" }}>
              Todavía no hay consultas en este periodo.
            </p>
          )}
        </div>
      </div>
    </div>
  );
}

function Tile({ numero, etiqueta, sub }: { numero: string; etiqueta: string; sub?: string }) {
  return (
    <div className="rounded-lg px-3 py-2.5" style={{ background: "var(--bg)", border: "1px solid var(--line)" }}>
      <p className="text-xl font-semibold tabular-nums">{numero}</p>
      <p className="text-[11px]" style={{ color: "var(--ink-soft)" }}>
        {etiqueta}
        {sub && <span className="block text-[10px]">{sub}</span>}
      </p>
    </div>
  );
}

function Listas({ datos }: { datos: EstadisticasResumen }) {
  return (
    <div className="mt-4 space-y-4">
      {datos.documentosMasCitados.length > 0 && (
        <Bloque titulo="Documentos más citados">
          {datos.documentosMasCitados.slice(0, 6).map((d) => (
            <Fila key={d.documento} etiqueta={d.documento} cuenta={`${d.citas}`} />
          ))}
        </Bloque>
      )}
      {datos.topPreguntas.length > 0 && (
        <Bloque titulo="Lo más preguntado">
          {datos.topPreguntas.slice(0, 6).map((p) => (
            <Fila key={p.pregunta} etiqueta={p.pregunta} cuenta={p.veces > 1 ? `×${p.veces}` : ""} />
          ))}
        </Bloque>
      )}
      {datos.ultimasSinCobertura.length > 0 && (
        <Bloque titulo={`Sin respuesta (${datos.ultimasSinCobertura.length})`} sub="El asistente no encontró nada en los documentos." scroll>
          {datos.ultimasSinCobertura.map((m, i) => (
            <Fila key={i} etiqueta={m.pregunta} cuenta="" detalle={m.dominio ?? undefined} />
          ))}
        </Bloque>
      )}
      {datos.ultimasMalas.length > 0 && (
        <Bloque titulo={`Pulgar abajo (${datos.ultimasMalas.length})`} sub="El usuario marcó la respuesta como no útil." scroll>
          {datos.ultimasMalas.map((m, i) => (
            <Fila key={i} etiqueta={m.pregunta} cuenta="" detalle={m.dominio ?? undefined} />
          ))}
        </Bloque>
      )}
    </div>
  );
}

function Bloque({ titulo, sub, scroll, children }: { titulo: string; sub?: string; scroll?: boolean; children: React.ReactNode }) {
  return (
    <section className="rounded-lg px-3 py-2.5" style={{ background: "var(--bg)", border: "1px solid var(--line)" }}>
      <h3 className="text-xs font-semibold">{titulo}</h3>
      {sub && (
        <p className="mb-1 text-[11px]" style={{ color: "var(--ink-soft)" }}>
          {sub}
        </p>
      )}
      <div className={scroll ? "max-h-64 divide-y overflow-y-auto" : "divide-y"} style={{ borderColor: "var(--line)" }}>
        {children}
      </div>
    </section>
  );
}

function Fila({ etiqueta, cuenta, detalle }: { etiqueta: string; cuenta: string; detalle?: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3 py-1.5 text-xs">
      <span className="min-w-0">
        <span className="block truncate" title={etiqueta}>
          {etiqueta}
        </span>
        {detalle && (
          <span className="block text-[10px]" style={{ color: "var(--ink-soft)" }}>
            {detalle}
          </span>
        )}
      </span>
      {cuenta && <span className="shrink-0 tabular-nums" style={{ color: "var(--ink-soft)" }}>{cuenta}</span>}
    </div>
  );
}
