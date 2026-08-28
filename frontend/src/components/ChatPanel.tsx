import { useEffect, useRef, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { useApp } from "../state/AppContext";
import MessageBubble from "./MessageBubble";
import { api } from "../lib/api";
import type { ModeloDisponible, NivelRazonamiento } from "../lib/types";

/** Panel central de chat: mensajes, composer y estado vacío con forma de onda. */
export default function ChatPanel() {
  const { chat, preguntar, detenerGeneracion, conversacionActiva, documentos } = useApp();
  const [texto, setTexto] = useState("");
  const [modelos, setModelos] = useState<ModeloDisponible[]>([]);
  const [modelo, setModelo] = useState(() => localStorage.getItem("rag-modelo") ?? "");
  const [razonamiento, setRazonamiento] = useState<NivelRazonamiento>(
    () => (localStorage.getItem("rag-razonamiento") as NivelRazonamiento | null) ?? "off",
  );
  const [perfilRapido, setPerfilRapido] = useState(
    () => localStorage.getItem("rag-perfil") === "fast",
  );
  const finRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    finRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [chat.mensajes.length, chat.mensajes[chat.mensajes.length - 1]?.contenido]);

  useEffect(() => {
    void api.listarModelos().then((disponibles) => {
      const seleccionables = disponibles.filter((m) => m.seleccionable);
      setModelos(seleccionables);
      if (modelo && !seleccionables.some((m) => `${m.proveedor}:${m.modelo}` === modelo)) setModelo("");
    }).catch(() => setModelos([]));
  }, [modelo]);

  const hayDocumentos = documentos.some((d) => d.estado === "listo");

  const enviar = async () => {
    const pregunta = texto.trim();
    if (!pregunta || chat.enviando) return;
    setTexto("");
    await preguntar(pregunta, modelo || undefined, razonamiento, perfilRapido ? "fast" : "normal");
  };

  return (
    <section className="flex min-h-0 min-w-0 flex-1 flex-col">
      <div className="flex items-center justify-between px-4 pt-3 md:px-8">
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate text-xs" style={{ color: "var(--ink-soft)" }}>
            {conversacionActiva ? conversacionActiva.titulo : "Conversación nueva"}
          </span>
          {modelos.length > 0 && (
            <label className="flex shrink-0 items-center gap-1 text-[10px] theme-ink-soft" title="Modelo de generación">
              <span className="hidden sm:inline">modelo</span>
              <select
                value={modelo}
                onChange={(e) => {
                  setModelo(e.target.value);
                  localStorage.setItem("rag-modelo", e.target.value);
                }}
                disabled={chat.enviando}
                className="modelo-select max-w-44 rounded-md px-1.5 py-1 text-[10px] outline-none"
                style={{ border: "1px solid var(--line)", color: "var(--ink)" }}
                aria-label="Elegir modelo"
              >
                <option value="">Predeterminado + fallback</option>
                {modelos.map((m) => (
                  <option key={`${m.proveedor}:${m.modelo}`} value={`${m.proveedor}:${m.modelo}`}>
                    {m.proveedor} · {m.nombre}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label className="flex shrink-0 items-center gap-1 text-[10px] theme-ink-soft" title="Nivel de razonamiento">
            <span className="hidden sm:inline">pensamiento</span>
            <select
              value={razonamiento}
              disabled={chat.enviando}
              onChange={(e) => {
                const valor = e.target.value as NivelRazonamiento;
                setRazonamiento(valor);
                localStorage.setItem("rag-razonamiento", valor);
              }}
              className="modelo-select max-w-28 rounded-md px-1.5 py-1 text-[10px] outline-none"
              aria-label="Nivel de razonamiento"
            >
              <option value="off">apagado</option>
              <option value="low">bajo</option>
              <option value="medium">medio</option>
              <option value="high">alto</option>
            </select>
          </label>
          <label className="flex shrink-0 items-center gap-1 text-[10px] theme-ink-soft" title="Reduce pasos costosos de recuperación y verificación">
            <input
              type="checkbox"
              checked={perfilRapido}
              disabled={chat.enviando}
              onChange={(e) => {
                setPerfilRapido(e.target.checked);
                localStorage.setItem("rag-perfil", e.target.checked ? "fast" : "normal");
              }}
              aria-label="Activar perfil rápido"
            />
            <span>rápido</span>
          </label>
        </div>
        {conversacionActiva && chat.mensajes.length > 0 && (
          <button
            onClick={() => {
              import("../lib/export").then(({ exportarConversacionMarkdown, descargar }) => {
                descargar(
                  `${conversacionActiva.titulo.replace(/[^\w\s-]/g, "").slice(0, 40)}.md`,
                  exportarConversacionMarkdown(conversacionActiva, chat.mensajes),
                );
              });
            }}
            className="rounded px-2 py-0.5 text-[11px]"
            style={{ border: "1px solid var(--line)", color: "var(--ink-soft)" }}
            data-testid="exportar"
          >
            ↓ Exportar .md
          </button>
        )}
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-4 py-6 md:px-8">
        <div className="mx-auto max-w-3xl space-y-5">
          {chat.mensajes.length === 0 && <EstadoVacio conDocumentos={hayDocumentos} />}
          <AnimatePresence initial={false}>
            {chat.mensajes.map((m) => (
              <motion.div
                key={m.id}
                initial={{ opacity: 0, y: 12 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.25 }}
              >
                <MessageBubble mensaje={m} />
              </motion.div>
            ))}
          </AnimatePresence>
          {chat.actividad.length > 0 && (
            <div className="flex flex-wrap gap-1.5" data-testid="actividad-agente">
              {chat.actividad.map((paso, i) => (
                <motion.span
                  key={`${i}-${paso}`}
                  initial={{ opacity: 0, y: 6 }}
                  animate={{ opacity: 1, y: 0 }}
                  className="rounded-full px-2.5 py-1 text-[11px]"
                  style={{
                    border: "1px solid var(--line)",
                    background: "var(--bg-elev)",
                    color: "var(--ink-soft)",
                  }}
                >
                  {i === chat.actividad.length - 1 && <OndaActivaMini />} {paso}
                </motion.span>
              ))}
            </div>
          )}
          {chat.metricas && !chat.enviando && (
            <div className="flex flex-wrap gap-x-3 gap-y-1 text-[10px] theme-ink-soft" data-testid="metricas-generacion">
              <span>{chat.metricas.tokens}{chat.metricas.tokensEstimados ? " tokens aprox." : " tokens"}</span>
              <span>{(chat.metricas.tokens / Math.max(chat.metricas.generacionMs / 1000, 0.001)).toFixed(1)} tokens/s</span>
              <span>generación {(chat.metricas.generacionMs / 1000).toFixed(1)} s</span>
              <span>total {(chat.metricas.totalMs / 1000).toFixed(1)} s</span>
              {chat.metricas.modelo && <span className="truncate">modelo {chat.metricas.modelo}</span>}
              {chat.metricas.proveedor && <span>{chat.metricas.proveedor}{chat.metricas.fallback ? " · fallback" : ""}</span>}
            </div>
          )}
          {chat.error && (
            <div
              className="rounded-md px-4 py-2.5 text-sm"
              style={{ background: "color-mix(in oklab, var(--accent-b) 14%, transparent)", color: "var(--accent-b)" }}
              role="alert"
            >
              {chat.error}
            </div>
          )}
          <div ref={finRef} />
        </div>
      </div>

      <div className="px-4 pb-5 pt-1 md:px-8">
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void enviar();
          }}
          className="mx-auto flex max-w-3xl items-end gap-2"
        >
          <div
            className="flex w-full items-center gap-2 rounded-xl px-3 py-2"
            style={{ background: "var(--bg-elev)", border: "1px solid var(--line)" }}
          >
            {chat.enviando && <OndaActiva />}
            <textarea
              value={texto}
              onChange={(e) => setTexto(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  void enviar();
                }
              }}
              rows={1}
              placeholder={
                conversacionActiva ? "Escribe tu pregunta…" : "Pregunta sobre RRHH, mantenimiento u onboarding…"
              }
              className="max-h-40 w-full resize-none bg-transparent outline-none"
              style={{ color: "var(--ink)" }}
              data-testid="composer"
            />
          </div>
          <button
            type={chat.enviando ? "button" : "submit"}
            disabled={!chat.enviando && !texto.trim()}
            aria-label={chat.enviando ? "Detener respuesta" : "Enviar"}
            onClick={chat.enviando ? detenerGeneracion : undefined}
            className="btn-accent-a rounded-xl px-4 py-2.5 font-bold disabled:opacity-40"
          >
            {chat.enviando ? "■" : "➤"}
          </button>
        </form>
        <p className="mx-auto mt-2 max-w-3xl text-[11px]" style={{ color: "var(--ink-soft)" }}>
          Las respuestas se generan exclusivamente a partir de los documentos indexados, con citas verificables.
        </p>
      </div>
    </section>
  );
}

function EstadoVacio({ conDocumentos }: { conDocumentos: boolean }) {
  return (
    <div className="flex flex-col items-center py-20 text-center" data-testid="estado-vacio">
      <OndaDecorativa />
      <h2 className="mt-6 text-lg font-semibold">Encuentra la señal en el ruido</h2>
      <p className="mt-2 max-w-md text-sm theme-ink-soft">
        {conDocumentos
          ? "Pregunta en lenguaje natural. El director decidirá qué agente especializado consultar."
          : "Sube un documento en las pestañas de Recursos Humanos, Mantenimiento u Onboarding para empezar."}
      </p>
    </div>
  );
}

export function OndaDecorativa() {
  const alturas = [0.4, 0.9, 0.55, 1, 0.35, 0.8, 0.5];
  return (
    <div className="flex h-10 items-center gap-1" aria-hidden>
      {alturas.map((h, i) => (
        <span
          key={i}
          className="wave-bar inline-block w-1 rounded-full"
          style={{
            height: `${h * 100}%`,
            background: i % 2 ? "var(--accent-b)" : "var(--accent-a)",
            animationDelay: `${i * 0.12}s`,
          }}
        />
      ))}
    </div>
  );
}

function OndaActivaMini() {
  return (
    <span className="wave-bar mr-1 inline-block h-2 w-0.5 rounded-full" style={{ background: "var(--accent-a)" }} aria-hidden />
  );
}

function OndaActiva() {
  const alturas = [0.5, 1, 0.4];
  return (
    <div className="flex h-4 items-center gap-0.5 pr-1" aria-hidden>
      {alturas.map((h, i) => (
        <span
          key={i}
          className="wave-bar inline-block w-0.5 rounded-full"
          style={{
            height: `${h * 100}%`,
            background: "var(--accent-a)",
            animationDelay: `${i * 0.15}s`,
          }}
        />
      ))}
    </div>
  );
}
