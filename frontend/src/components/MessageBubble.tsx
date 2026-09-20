import { lazy, memo, Suspense, useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Check, Copy, Square, Volume2 } from "lucide-react";
import { useApp } from "../state/AppContext";
import { dividirPorCitas } from "../lib/api";
import {
  copiarAlPortapapeles,
  detenerLectura,
  leerEnVozAlta,
  soportaSintesis,
  textoPlanoMarkdown,
} from "../lib/voz";
import type { Fuente, MensajeChat, RecursoMedia, TrazaPipeline } from "../lib/types";

const MermaidDiagrama = lazy(() => import("./MermaidDiagrama"));

/** Burbuja de mensaje: markdown con sellos de cita, banner de verificación y acceso a la traza. */
function MessageBubble({ mensaje }: { mensaje: MensajeChat }) {
  const esUsuario = mensaje.rol === "user";

  return (
    <div className={`flex ${esUsuario ? "justify-end" : "justify-start"}`} data-testid={`mensaje-${mensaje.rol}`}>
      <div
        className="stamp-enter max-w-[92%] rounded-xl px-4 py-3 text-sm leading-relaxed md:max-w-[85%]"
        style={{
          background: esUsuario
            ? "color-mix(in oklab, var(--accent-a) 12%, var(--bg-elev))"
            : "var(--bg-elev)",
          border: "1px solid var(--line)",
        }}
      >
        {esUsuario ? (
          <p className="whitespace-pre-wrap">{mensaje.contenido}</p>
        ) : (
          <>
            <RespuestaAsistente mensaje={mensaje} />
            {!esUsuario && !mensaje.pendiente && <AccionesMensaje mensaje={mensaje} />}
            {!esUsuario && !mensaje.pendiente && (mensaje.verificacion || mensaje.traza) && (
              <div className="mt-2 flex flex-wrap items-center gap-2 border-t pt-2" style={{ borderColor: "var(--line)" }}>
                <VerificacionBadge verificacion={mensaje.verificacion} />
                {mensaje.traza && <BotonTraza traza={mensaje.traza} />}
              </div>
            )}
            {!esUsuario && !mensaje.pendiente && mensaje.metricas && <BotonEstadisticas metricas={mensaje.metricas} />}
            {mensaje.revisionContenido && <RevisionSugerida mensaje={mensaje} />}
          </>
        )}
      </div>
    </div>
  );
}

function BotonEstadisticas({ metricas }: { metricas: NonNullable<MensajeChat["metricas"]> }) {
  const tokensPorSegundo = metricas.tokens / Math.max(metricas.generacionMs / 1000, 0.001);
  return (
    <div className="relative mt-2 flex justify-end border-t pt-2" style={{ borderColor: "var(--line)" }}>
      <button
        type="button"
        aria-label="Ver estadísticas de la respuesta"
        className="grupo-estadisticas flex h-5 w-5 items-center justify-center rounded-full text-[11px] font-bold"
        style={{ border: "1px solid var(--line)", color: "var(--ink-soft)" }}
      >
        ?
      </button>
      <div
        role="tooltip"
        className="tooltip-estadisticas pointer-events-none absolute bottom-7 right-0 z-20 w-56 rounded-lg p-3 text-[11px] shadow-xl"
        style={{ background: "var(--bg-elev)", border: "1px solid var(--line)", color: "var(--ink)" }}
      >
        <strong className="mb-1 block text-xs">Estadísticas de respuesta</strong>
        <span className="block">Solicitado: {metricas.proveedorSolicitado ?? "predeterminado"} · {metricas.modeloSolicitado ?? "cadena configurada"}</span>
        <span className="block">Utilizado: {metricas.proveedor ?? "desconocido"} · {metricas.modelo ?? "desconocido"}</span>
        {metricas.razonamiento && <span className="block">Razonamiento: {metricas.razonamiento}</span>}
        <span className="block">Tokens: {metricas.tokens}{metricas.tokensEstimados ? " aprox." : ""}</span>
        <span className="block">Velocidad: {tokensPorSegundo.toFixed(1)} tokens/s</span>
        <span className="block">Generación: {(metricas.generacionMs / 1000).toFixed(2)} s</span>
        <span className="block">Tiempo total: {(metricas.totalMs / 1000).toFixed(2)} s</span>
        {metricas.fallback && <span className="mt-1 block" style={{ color: "var(--accent-b)" }}>Se utilizó fallback</span>}
      </div>
    </div>
  );
}

// memo: el padre re-renderiza por cada token SSE; los mensajes estables conservan
// su referencia y no se repintan (solo el borrador en curso cambia de identidad)
export default memo(MessageBubble);

function RespuestaAsistente({ mensaje }: { mensaje: MensajeChat }) {
  const contenido = mensaje.contenido;
  const partes = useMemo(() => dividirPorCitas(contenido), [contenido]);
  const citas = useMemo(
    () => [...new Set(partes.filter((p) => p.tipo === "cita").map((p) => p.valor))],
    [partes],
  );
  const fuentes = mensaje.fuentes ?? [];

  if (mensaje.pendiente && !mensaje.contenido) return <OndaEscritura />;

  return (
    <div>
      <div className="prose-rag"><ReactMarkdown remarkPlugins={[remarkGfm]} components={{ a: EnlaceSeguro }}>
        {mensaje.contenido}</ReactMarkdown></div>

      {mensaje.opcionesAclaracion && mensaje.opcionesAclaracion.length > 0 && (
        <OpcionesAclaracion mensaje={mensaje} />
      )}

      {/* sellos de cita interactivos (todas las citas presentes en el texto) */}
      {citas.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1.5 border-t pt-2" style={{ borderColor: "var(--line)" }}>
          {citas.map((n) => {
            const idx = parseInt(n, 10) - 1;
            const f: Fuente | undefined = fuentes[idx];
            if (!f) return null;
            return <SelloCita key={n} numero={n} fuente={f} />;
          })}
        </div>
      )}

      {fuentes.length > 0 && <FuentesDeRespuesta fuentes={fuentes} />}

      {mensaje.recursos && mensaje.recursos.length > 0 && (
        <RecursosMedia recursos={mensaje.recursos} />
      )}

      <BloquesMermaid contenido={mensaje.contenido} />
    </div>
  );
}

function EnlaceSeguro({ href, children }: { href?: string; children?: ReactNode }) {
  if (!href) return <span>{children}</span>;
  let segura = false;
  try {
    const esquema = new URL(href, window.location.origin).protocol.toLowerCase();
    segura = esquema === "http:" || esquema === "https:";
  } catch {
    segura = href.startsWith("/");
  }
  if (!segura) return <span>{children}</span>;
  return (
    <a href={href} target="_blank" rel="noopener noreferrer">
      {children}
    </a>
  );
}

function RecursosMedia({ recursos }: { recursos: RecursoMedia[] }) {
  return (
    <div className="mt-2 border-t pt-2" style={{ borderColor: "var(--line)" }}>
      <p className="mb-1.5 text-[11px] font-semibold uppercase tracking-wide theme-ink-soft">Recursos</p>
      <div className="flex flex-col gap-1.5">
        {recursos.map((r) => (
          r.tipo === "video"
            ? <TarjetaVideo key={r.url} recurso={r} />
            : <a
                key={r.url}
                href={r.url}
                target="_blank"
                rel="noopener noreferrer"
                className="text-xs underline"
                style={{ color: "var(--accent-a)" }}
              >
                {r.contexto || r.url}
              </a>
        ))}
      </div>
    </div>
  );
}

function TarjetaVideo({ recurso }: { recurso: RecursoMedia }) {
  const [reproducir, setReproducir] = useState(false);
  const procedencia = [recurso.documento, recurso.pagina ? `pág. ${recurso.pagina}` : null]
    .filter(Boolean)
    .join(" · ");
  return (
    <div className="rounded-lg p-2.5" style={{ border: "1px solid var(--line)" }}>
      <p className="text-xs font-medium">{recurso.contexto || "Vídeo"}</p>
      {procedencia && <p className="mt-0.5 text-[11px] theme-ink-soft">{procedencia}</p>}
      {reproducir ? (
        <video controls preload="none" playsInline src={recurso.url} className="mt-2 w-full rounded-md" />
      ) : (
        <div className="mt-2 flex items-center gap-2">
          <button
            type="button"
            onClick={() => setReproducir(true)}
            aria-label={`Reproducir ${recurso.contexto || "vídeo"}`}
            className="rounded-full px-3 py-1 text-[11px] font-semibold cursor-pointer"
            style={{ border: "1px solid var(--accent-a)", color: "var(--accent-a)" }}
          >
            ▶ Ver vídeo
          </button>
          <a
            href={recurso.url}
            target="_blank"
            rel="noopener noreferrer"
            className="text-[11px] underline theme-ink-soft"
          >
            Abrir en pestaña nueva
          </a>
        </div>
      )}
    </div>
  );
}

function OpcionesAclaracion({ mensaje }: { mensaje: MensajeChat }) {
  const { preguntar, chat } = useApp();
  const opciones = mensaje.opcionesAclaracion ?? [];
  if (opciones.length === 0) return null;
  return (
    <div className="mt-2 flex flex-wrap gap-1.5" role="group" aria-label="Opciones de aclaración">
      {opciones.map((op) => (
        <button
          key={op.valor}
          type="button"
          disabled={chat.enviando}
          onClick={() => void preguntar(op.valor)}
          className="rounded-full px-2.5 py-1 text-[11px]"
          style={{
            border: "1px solid var(--accent-a)",
            color: "var(--accent-a)",
            background: "color-mix(in oklab, var(--accent-a) 10%, transparent)",
          }}
        >
          {op.texto}
        </button>
      ))}
    </div>
  );
}

function FuentesDeRespuesta({ fuentes }: { fuentes: Fuente[] }) {
  return (
    <details className="mt-3 border-t pt-2" style={{ borderColor: "var(--line)" }}>
      <summary className="cursor-pointer text-xs font-semibold theme-ink-soft">
        Ver {fuentes.length} {fuentes.length === 1 ? "fuente" : "fuentes"}
      </summary>
      <div className="mt-2 space-y-2">
        {fuentes.map((fuente) => (
          <div key={fuente.chunkId} className="rounded-md px-2.5 py-2" style={{ border: "1px solid var(--line)" }}>
            <div className="flex items-baseline gap-2 text-xs">
              <span className="citation-stamp !transform-none">{fuente.indice}</span>
              <strong className="truncate">{fuente.documentoNombre}</strong>
              {fuente.pagina && <span className="ml-auto shrink-0 theme-ink-soft">Página {fuente.pagina}</span>}
            </div>
            <p className="mt-1 whitespace-pre-wrap text-[11px] leading-relaxed theme-ink-soft">{fuente.fragmento}</p>
          </div>
        ))}
      </div>
    </details>
  );
}

function SelloCita({ numero, fuente }: { numero: string; fuente: Fuente }) {
  return (
    <button
      className="citation-stamp"
      data-used={String(fuente.usada)}
      onClick={() =>
        window.dispatchEvent(
          new CustomEvent("fuente-seleccionada", {
            detail: { chunkId: fuente.chunkId, fuente },
          }),
        )
      }
      title={`${fuente.documentoNombre}${fuente.pagina ? ` · pág. ${fuente.pagina}` : ""} — ${fuente.fragmento.slice(0, 140)}…`}
    >
      {numero}
    </button>
  );
}

function BloquesMermaid({ contenido }: { contenido: string }) {
  const bloques = [...contenido.matchAll(/```mermaid\n([\s\S]*?)```/g)];
  if (bloques.length === 0) return null;
  return (
    <Suspense fallback={<p className="text-xs theme-ink-soft">cargando diagrama…</p>}>
      {bloques.map((b, i) => (
        <MermaidDiagrama key={i} codigo={b[1]} />
      ))}
    </Suspense>
  );
}

export function VerificacionBadge({ verificacion }: { verificacion?: MensajeChat["verificacion"] }) {
  if (!verificacion) return null;
  if (verificacion.verdict === "supported")
    return (
      <span className="text-xs" style={{ color: "var(--accent-a)" }} title="Comprobado contra las fuentes">
        ✓ Verificado
      </span>
    );
  if (verificacion.verdict === "error")
    return <span className="text-xs theme-ink-soft">Verificación no completada</span>;
  return (
    <span className="text-xs" style={{ color: "var(--accent-b)" }} title={verificacion.critique ?? ""}>
      ⚠ Revisar — {verificacion.critique?.slice(0, 60)}
    </span>
  );
}

function RevisionSugerida({ mensaje }: { mensaje: MensajeChat }) {
  const { aceptarRevision } = useApp();
  const [visible, setVisible] = useState(true);
  if (!visible) return null;

  return (
    <div
      className="mt-2 rounded-md p-3"
      style={{ background: "color-mix(in oklab, var(--accent-b) 10%, transparent)", border: "1px solid var(--accent-b)" }}
      data-testid="revision-sugerida"
    >
      <p className="mb-2 text-xs font-semibold" style={{ color: "var(--accent-b)" }}>
        Sugerencia de revisión disponible
      </p>
      <p className="max-h-32 overflow-y-auto whitespace-pre-wrap text-xs opacity-80">
        {mensaje.revisionContenido}
      </p>
      <div className="mt-2 flex gap-2">
        <button
          onClick={() => void aceptarRevision(mensaje.id)}
          className="btn-accent-a rounded px-2 py-1 text-xs font-semibold"
        >
          Aplicar revisión
        </button>
        <button onClick={() => setVisible(false)} className="rounded px-2 py-1 text-xs" style={{ color: "var(--ink-soft)" }}>
          Descartar
        </button>
      </div>
    </div>
  );
}

function BotonTraza({ traza }: { traza: TrazaPipeline }) {
  return (
    <button
      className="rounded px-2 py-0.5 text-xs"
      style={{ border: "1px solid var(--line)", color: "var(--accent-a)" }}
      onClick={() => window.dispatchEvent(new CustomEvent("mostrar-traza", { detail: traza }))}
    >
      ⏱ Traza ({traza.modo})
    </button>
  );
}

/** Acciones por mensaje asistente: escuchar en voz alta y copiar al portapapeles. */
function AccionesMensaje({ mensaje }: { mensaje: MensajeChat }) {
  const [copiado, setCopiado] = useState(false);
  const [leyendo, setLeyendo] = useState(false);
  const leyendoRef = useRef(false);
  leyendoRef.current = leyendo;

  useEffect(() => () => { if (leyendoRef.current) detenerLectura(); }, []);

  const copiar = async () => {
    const ok = await copiarAlPortapapeles(textoPlanoMarkdown(mensaje.contenido));
    if (ok) {
      setCopiado(true);
      setTimeout(() => setCopiado(false), 2000);
    }
  };

  const alternarLectura = () => {
    if (leyendo) {
      detenerLectura();
      setLeyendo(false);
      return;
    }
    if (!textoPlanoMarkdown(mensaje.contenido)) return;
    setLeyendo(true);
    leerEnVozAlta(mensaje.id, textoPlanoMarkdown(mensaje.contenido), () => setLeyendo(false));
  };

  return (
    <div
      className="mt-2 flex flex-wrap items-center gap-1.5 border-t pt-1.5 text-[11px]"
      style={{ borderColor: "var(--line)" }}
    >
      {soportaSintesis() && (
        <button
          type="button"
          aria-label={leyendo ? "Detener lectura" : "Escuchar respuesta"}
          title={leyendo ? "Detener lectura" : "Escuchar respuesta"}
          onClick={alternarLectura}
          className="btn-iconte"
          style={{ color: leyendo ? "var(--accent-b)" : "var(--ink-soft)" }}
        >
          {leyendo ? <Square size={14} /> : <Volume2 size={14} />}
        </button>
      )}
      <button
        type="button"
        aria-label={copiado ? "Copiado" : "Copiar respuesta"}
        title={copiado ? "Copiado" : "Copiar al portapapeles"}
        onClick={() => void copiar()}
        className="btn-iconte"
        style={{ color: copiado ? "var(--accent-a)" : "var(--ink-soft)" }}
      >
        {copiado ? <Check size={14} /> : <Copy size={14} />}
      </button>
    </div>
  );
}

function OndaEscritura() {
  return (
    <div className="flex h-5 items-center gap-0.5 py-1" aria-label="generando respuesta">
      {[0.6, 1, 0.45, 0.9, 0.35].map((h, i) => (
        <span
          key={i}
          className="wave-bar inline-block w-0.5 rounded-full"
          style={{ height: `${h * 100}%`, background: "var(--accent-a)", animationDelay: `${i * 0.12}s` }}
        />
      ))}
    </div>
  );
}
