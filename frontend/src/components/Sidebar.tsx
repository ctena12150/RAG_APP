import { useEffect, useMemo, useRef, useState } from "react";
import { motion } from "framer-motion";
import { useApp } from "../state/AppContext";
import { DOMINIOS, ETIQUETA_DOMINIO, type Documento } from "../lib/types";

type Pestana = "conversaciones" | "documentos";

export default function Sidebar({ onNavegar }: { onNavegar?: () => void }) {
  const {
    dominioActivo,
    setDominioActivo,
    documentos,
    folders,
    crearFolder,
    borrarFolder,
    subirDocumento,
    subidaActiva,
    errorSubida,
    conversaciones,
    conversacionActiva,
    abrirConversacion,
    nuevaConversacion,
    borrarConversacion,
  } = useApp();

  const [pestana, setPestana] = useState<Pestana>("conversaciones");
  const [filtroConv, setFiltroConv] = useState("");
  const [filtroDoc, setFiltroDoc] = useState("");
  const [nuevaCarpetaVisible, setNuevaCarpetaVisible] = useState(false);
  const inputCarpeta = useRef<HTMLInputElement>(null);
  const inputArchivo = useRef<HTMLInputElement>(null);

  const docsDelDominio = useMemo(
    () =>
      documentos
        .filter((d) => dominioActivo === "todas" || d.dominio === dominioActivo)
        .filter((d) => d.nombreArchivo.toLowerCase().includes(filtroDoc.toLowerCase())),
    [documentos, dominioActivo, filtroDoc],
  );

  const convsFiltradas = useMemo(
    () =>
      (conversaciones.length >= 5
        ? conversaciones.filter((c) => c.titulo.toLowerCase().includes(filtroConv.toLowerCase()))
        : conversaciones),
    [conversaciones, filtroConv],
  );

  const carpetas = folders.filter((f) => dominioActivo === "todas" || f.dominio === dominioActivo);
  const sinCarpeta = (docs: Documento[]) => docs.filter((d) => !d.folderId);

  return (
    <div className="flex h-full flex-col text-sm">
      <div className="px-4 py-4" style={{ borderBottom: "1px solid var(--line)" }}>
        <span style={{ fontFamily: "var(--font-display)", fontSize: "1.2rem" }}>
          Evidentia<span className="accent-a">·</span>RAG
        </span>
      </div>

      <div className="flex" style={{ borderBottom: "1px solid var(--line)" }}>
        {(["conversaciones", "documentos"] as Pestana[]).map((p) => (
          <button
            key={p}
            onClick={() => setPestana(p)}
            className="flex-1 py-2 capitalize"
            style={{
              color: pestana === p ? "var(--accent-a)" : "var(--ink-soft)",
              borderBottom: pestana === p ? "2px solid var(--accent-a)" : "2px solid transparent",
            }}
          >
            {p}
          </button>
        ))}
      </div>

      {pestana === "conversaciones" ? (
        <div className="min-h-0 flex-1 overflow-y-auto p-3">
          <button
            onClick={() => {
              void nuevaConversacion();
              onNavegar?.();
            }}
            className="btn-accent-a mb-3 w-full rounded-md py-1.5 font-semibold"
          >
            + Nueva conversación
          </button>
          {conversaciones.length >= 5 && (
            <input
              value={filtroConv}
              onChange={(e) => setFiltroConv(e.target.value)}
              placeholder="Filtrar por título…"
              className="mb-2 w-full rounded-md px-2 py-1"
              style={{ background: "var(--bg)", border: "1px solid var(--line)" }}
            />
          )}
          <ul className="space-y-1">
            {convsFiltradas.map((c, i) => (
              <motion.li
                key={c.id}
                initial={{ opacity: 0, y: 6 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: Math.min(i * 0.03, 0.25) }}
                className={`group flex items-center justify-between rounded-md px-2 py-1.5 ${
                  conversacionActiva?.id === c.id ? "" : ""
                }`}
                style={{
                  background:
                    conversacionActiva?.id === c.id
                      ? "color-mix(in oklab, var(--accent-a) 14%, transparent)"
                      : undefined,
                  cursor: "pointer",
                }}
                onClick={() => {
                  void abrirConversacion(c.id);
                  onNavegar?.();
                }}
              >
                <span className="truncate">{c.titulo}</span>
                <button
                  aria-label={`Borrar ${c.titulo}`}
                  className="opacity-0 group-hover:opacity-100"
                  style={{ color: "var(--accent-b)" }}
                  onClick={(e) => {
                    e.stopPropagation();
                    void borrarConversacion(c.id);
                  }}
                >
                  ✕
                </button>
              </motion.li>
            ))}
          </ul>
        </div>
      ) : (
        <div className="min-h-0 flex-1 overflow-y-auto p-3">
          {/* pestañas de dominios */}
          <div className="mb-3 flex flex-wrap gap-1">
            {[...DOMINIOS, "todas" as const].map((d) => (
              <button
                key={d}
                onClick={() => setDominioActivo(d)}
                data-active={dominioActivo === d}
                className="rounded-full px-2.5 py-1 text-xs"
                style={{
                  border: `1px solid ${dominioActivo === d ? "var(--accent-a)" : "var(--line)"}`,
                  color: dominioActivo === d ? "var(--accent-a)" : "var(--ink-soft)",
                }}
              >
                {d === "todas" ? "Todas" : ETIQUETA_DOMINIO[d]}
              </button>
            ))}
          </div>

          <input
            ref={inputArchivo}
            type="file"
            accept=".pdf,.docx,.txt,.md"
            hidden
            onChange={async (e) => {
              const file = e.target.files?.[0];
              if (file && dominioActivo !== "todas") await subirDocumento(file, dominioActivo);
              e.target.value = "";
            }}
          />
          <div className="mb-3 flex gap-2">
            <button
              onClick={() => inputArchivo.current?.click()}
              disabled={dominioActivo === "todas"}
              className="btn-accent-a flex-1 rounded-md py-1.5 font-semibold disabled:opacity-40"
            >
              ↑ Subir documento
            </button>
            <button
              onClick={() => setNuevaCarpetaVisible(true)}
              title="Nueva carpeta"
              className="rounded-md px-2.5"
              style={{ border: "1px solid var(--line)" }}
            >
              🗀+
            </button>
          </div>
          {subidaActiva && (
            <div className="mb-3" data-testid="progreso-subida">
              <div className="mb-1 flex items-center justify-between gap-2 text-[11px] theme-ink-soft">
                <span className="truncate" title={subidaActiva.nombreArchivo}>
                  {subidaActiva.fase === "subiendo" ? "Subiendo" : "Procesando"}{" "}
                  {subidaActiva.nombreArchivo}
                  {subidaActiva.fase === "subiendo" &&
                    ` — ${Math.round(subidaActiva.progresoSubida * 100)}%`}
                  {subidaActiva.fase === "procesando" && "…"}
                </span>
                {subidaActiva.fase === "procesando" && <SegundosTranscurridos />}
              </div>
              {subidaActiva.fase === "subiendo" ? (
                <div className="h-1.5 overflow-hidden rounded-full" style={{ background: "var(--line)" }}>
                  <div
                    className="h-full rounded-full transition-[width] duration-150"
                    style={{
                      width: `${Math.max(4, Math.round(subidaActiva.progresoSubida * 100))}%`,
                      background: "var(--accent-a)",
                    }}
                  />
                </div>
              ) : (
                <div className="h-1.5 overflow-hidden rounded-full" style={{ background: "var(--line)" }}>
                  <div
                    className="barra-indeterminada-recorrido h-full w-1/3 rounded-full"
                    style={{ background: "var(--accent-a)" }}
                  />
                </div>
              )}
            </div>
          )}
          {errorSubida && (
            <p className="mb-2 text-xs" role="alert" style={{ color: "var(--accent-b)" }}>
              {errorSubida}
            </p>
          )}
          {nuevaCarpetaVisible && (
            <form
              className="mb-2 flex gap-1"
              onSubmit={async (e) => {
                e.preventDefault();
                const nombre = inputCarpeta.current?.value.trim();
                if (nombre) {
                  await crearFolder(nombre);
                  setNuevaCarpetaVisible(false);
                  if (inputCarpeta.current) inputCarpeta.current.value = "";
                }
              }}
            >
              <input
                ref={inputCarpeta}
                autoFocus
                placeholder="Nombre de carpeta"
                className="w-full rounded-md px-2 py-1"
                style={{ background: "var(--bg)", border: "1px solid var(--line)" }}
              />
              <button type="submit" className="btn-accent-a rounded-md px-2">
                OK
              </button>
            </form>
          )}

          <input
            value={filtroDoc}
            onChange={(e) => setFiltroDoc(e.target.value)}
            placeholder="Filtrar por nombre…"
            className="mb-2 w-full rounded-md px-2 py-1"
            style={{ background: "var(--bg)", border: "1px solid var(--line)" }}
          />

          {dominioActivo !== "todas" && (
            <div className="mb-1 mt-3 flex items-center gap-1 text-xs theme-ink-soft">
              {ETIQUETA_DOMINIO[dominioActivo]}
            </div>
          )}

          {carpetas.map((f) => {
            const docsCarpeta = docsDelDominio.filter((d) => d.folderId === f.id);
            return (
              <div key={f.id} className="mb-2">
                <div className="group flex items-center justify-between px-1 py-1 text-xs">
                  <span className="theme-ink-soft">🗀 {f.nombre} ({docsCarpeta.length})</span>
                  <button
                    aria-label={`Borrar carpeta ${f.nombre}`}
                    className="opacity-0 group-hover:opacity-100"
                    style={{ color: "var(--accent-b)" }}
                    onClick={() => void borrarFolder(f.id)}
                    title="Los documentos pasan a 'sin categoría', nunca se borran"
                  >
                    ✕
                  </button>
                </div>
                <ListaDocumentos documentos={docsCarpeta} />
              </div>
            );
          })}

          {sinCarpeta(docsDelDominio).length > 0 && (carpetas.length > 0 || true) && (
            <>
              {carpetas.length > 0 && (
                <div className="mb-1 px-1 text-xs theme-ink-soft">Sin categoría</div>
              )}
              <ListaDocumentos documentos={sinCarpeta(docsDelDominio)} />
            </>
          )}
        </div>
      )}
    </div>
  );
}

function ListaDocumentos({ documentos }: { documentos: Documento[] }) {
  const { borrarDocumento, docResaltado } = useApp();

  return (
    <ul className="space-y-0.5">
      {documentos.map((d) => {
        const resaltado = d.id === docResaltado?.id ? docResaltado.estado : null;
        return (
          <li
            key={d.id}
            className={`group flex items-center gap-2 rounded-md px-2 py-1.5 hover:bg-black/5${
              resaltado ? " fila-flash" : ""
            }`}
            style={
              resaltado
                ? {
                    background:
                      resaltado === "listo"
                        ? "color-mix(in oklab, var(--accent-a) 16%, transparent)"
                        : "color-mix(in oklab, var(--accent-b) 16%, transparent)",
                  }
                : undefined
            }
          >
            <EstadoPunto estado={d.estado} />
            {resaltado === "listo" && (
              <span aria-hidden="true" style={{ color: "var(--accent-a)" }}>
                ✓
              </span>
            )}
            <span className="truncate" title={d.nombreArchivo}>
              {d.nombreArchivo}
            </span>
            {d.estado === "error" && (
              <span className="ml-auto truncate text-[10px]" style={{ color: "var(--accent-b)" }} title={d.errorMensaje ?? ""}>
                error
              </span>
            )}
            <button
              aria-label={`Borrar ${d.nombreArchivo}`}
              className="ml-auto shrink-0 opacity-0 group-hover:opacity-100"
              style={{ color: "var(--accent-b)" }}
              onClick={() => void borrarDocumento(d.id)}
            >
              ✕
            </button>
          </li>
        );
      })}
    </ul>
  );
}

function SegundosTranscurridos() {
  const [segundos, setSegundos] = useState(0);
  useEffect(() => {
    const t = setInterval(() => setSegundos((s) => s + 1), 1000);
    return () => clearInterval(t);
  }, []);
  return <span className="shrink-0 tabular-nums">{segundos}s</span>;
}

function EstadoPunto({ estado }: { estado: Documento["estado"] }) {
  const color =
    estado === "listo" ? "var(--accent-a)" : estado === "error" ? "var(--accent-b)" : "var(--ink-soft)";
  return (
    <span
      aria-label={`estado: ${estado}`}
      className="inline-block h-2 w-2 shrink-0 rounded-full"
      style={{ background: color, animation: estado === "procesando" || estado === "pendiente" ? "wave-pulse 1s infinite" : undefined }}
    />
  );
}
