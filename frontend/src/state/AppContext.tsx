import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import { api, describirAgente, streamChat } from "../lib/api";
import type { PerfilChat } from "../lib/api";
import type { Conversacion, Dominio, Documento, Folder, Fuente, MensajeChat, MetricasGeneracion, TrazaPipeline, Verificacion } from "../lib/types";

interface EstadoChat {
  mensajes: MensajeChat[];
  enviando: boolean;
  error: string | null;
  actividad: string[];
  metricas: MetricasGeneracion | null;
}

/** Subida en curso: fase de envío (con %) o de procesamiento asíncrono en el backend. */
export interface SubidaActiva {
  id: string;
  nombreArchivo: string;
  fase: "subiendo" | "procesando";
  progresoSubida: number;
}

/** Documento recién terminado (listo|error): la fila parpadea unos segundos. */
export interface DocResaltado {
  id: string;
  estado: "listo" | "error";
}

interface AppContextValue {
  vista: "landing" | "app";
  entrarApp: () => void;

  tema: "dark" | "light";
  alternarTema: () => void;

  dominioActivo: Dominio | "todas";
  setDominioActivo: (d: Dominio | "todas") => void;

  documentos: Documento[];
  folders: Folder[];
  refrescarDocumentos: () => Promise<void>;
  subirDocumento: (file: File, dominio: Dominio) => Promise<void>;
  borrarDocumento: (id: string) => Promise<void>;
  crearFolder: (nombre: string) => Promise<void>;
  borrarFolder: (id: string) => Promise<void>;
  subidaActiva: SubidaActiva | null;
  docResaltado: DocResaltado | null;
  errorSubida: string | null;

  conversaciones: Conversacion[];
  conversacionActiva: Conversacion | null;
  chat: EstadoChat;
  fuentesSeleccionadas: { fuentes: Fuente[]; traza?: TrazaPipeline | null; messageId: string } | null;
  seleccionarFuentes: (sel: AppContextValue["fuentesSeleccionadas"]) => void;

  refrescarConversaciones: () => Promise<void>;
  abrirConversacion: (id: string) => Promise<void>;
  nuevaConversacion: () => Promise<void>;
  borrarConversacion: (id: string) => Promise<void>;
  preguntar: (texto: string, modelo?: string, razonamiento?: string, perfil?: PerfilChat) => Promise<void>;
  detenerGeneracion: () => void;
  aceptarRevision: (messageId: string) => Promise<void>;
}

const AppContext = createContext<AppContextValue | null>(null);

export function useApp(): AppContextValue {
  const ctx = useContext(AppContext);
  if (!ctx) throw new Error("useApp debe usarse dentro de AppProvider");
  return ctx;
}

let contadorIds = 0;
const idTemporal = () => `tmp-${Date.now()}-${contadorIds++}`;

export function AppProvider({ children }: { children: ReactNode }) {
  const [vista, setVista] = useState<"landing" | "app">("landing");
  const [tema, setTema] = useState<"dark" | "light">(
    () => (localStorage.getItem("rag-theme") as "dark" | "light") ?? "dark",
  );
  const [dominioActivo, setDominioActivo] = useState<Dominio | "todas">("rrhh");
  const [documentos, setDocumentos] = useState<Documento[]>([]);
  const [folders, setFolders] = useState<Folder[]>([]);
  const [conversaciones, setConversaciones] = useState<Conversacion[]>([]);
  const [conversacionActiva, setConversacionActiva] = useState<Conversacion | null>(null);
  const [chat, setChat] = useState<EstadoChat>({ mensajes: [], enviando: false, error: null, actividad: [], metricas: null });
  const [fuentesSeleccionadas, setFuentesSeleccionadas] =
    useState<AppContextValue["fuentesSeleccionadas"]>(null);
  const [subidaActiva, setSubidaActiva] = useState<SubidaActiva | null>(null);
  const [docResaltado, setDocResaltado] = useState<DocResaltado | null>(null);
  const [errorSubida, setErrorSubida] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const detenidoRef = useRef(false);

  const detenerGeneracion = useCallback(() => {
    if (!abortRef.current) return;
    detenidoRef.current = true;
    abortRef.current.abort();
    setChat((c) => ({
      ...c,
      enviando: false,
      actividad: ["Respuesta detenida"],
      mensajes: c.mensajes.map((m) => (m.pendiente ? { ...m, pendiente: false, cancelada: true } : m)),
    }));
  }, []);

  useEffect(() => {
    document.documentElement.dataset.theme = tema;
    localStorage.setItem("rag-theme", tema);
  }, [tema]);

  useEffect(() => {
    document.documentElement.dataset.view = vista;
    localStorage.setItem("rag-view", vista);
  }, [vista]);

  const alternarTema = useCallback(() => {
    setTema((t) => (t === "dark" ? "light" : "dark"));
  }, []);

  const refrescarDocumentos = useCallback(async () => {
    try {
      const docs = await api.listarDocumentos();
      setDocumentos(docs);
      setFolders(await api.listarFolders());
    } catch {
      /* el panel muestra estado vacío */
    }
  }, []);

  const refrescarConversaciones = useCallback(async () => {
    try {
      setConversaciones(await api.listarConversaciones());
    } catch {
      /* noop */
    }
  }, []);

  useEffect(() => {
    if (vista === "app") {
      void refrescarDocumentos();
      void refrescarConversaciones();
    }
  }, [vista, refrescarDocumentos, refrescarConversaciones]);

  const subirDocumento = useCallback(
    async (file: File, dominio: Dominio) => {
      setErrorSubida(null);
      setDocResaltado(null);
      // el id real llega cuando responde el POST; mientras tanto la barra muestra el % de envío
      setSubidaActiva({ id: "", nombreArchivo: file.name, fase: "subiendo", progresoSubida: 0 });
      let doc: Documento;
      try {
        doc = await api.subirDocumento(file, dominio, {
          onProgresoSubida: (p) =>
            setSubidaActiva((s) => (s !== null && s.fase === "subiendo" ? { ...s, progresoSubida: p } : s)),
        });
      } catch (err) {
        setSubidaActiva(null);
        setErrorSubida(err instanceof Error ? err.message : "Error al subir el documento.");
        return;
      }
      await refrescarDocumentos();
      setSubidaActiva({ id: doc.id, nombreArchivo: file.name, fase: "procesando", progresoSubida: 1 });

      // polling SOLO del documento subido, sin tope corto: la ingesta de PDFs grandes
      // (embeddings vía VPS) puede tardar varios minutos
      let intervalo: ReturnType<typeof setInterval> | null = null;
      const terminar = (estadoFinal: DocResaltado["estado"]) => {
        if (intervalo) clearInterval(intervalo);
        clearTimeout(cap);
        setSubidaActiva((s) => (s?.id === doc.id ? null : s));
        void refrescarDocumentos();
        setDocResaltado({ id: doc.id, estado: estadoFinal });
        setTimeout(() => setDocResaltado((r) => (r?.id === doc.id ? null : r)), 6000);
      };
      const cap = setTimeout(() => {
        if (intervalo) clearInterval(intervalo);
        setSubidaActiva((s) => (s?.id === doc.id ? null : s));
      }, 600_000);

      intervalo = setInterval(async () => {
        try {
          const estado = await api.estadoDocumento(doc.id);
          if (estado.estado === "listo" || estado.estado === "error") {
            terminar(estado.estado === "error" ? "error" : "listo");
          }
        } catch {
          /* reintenta en el siguiente tick */
        }
      }, 1500);
    },
    [refrescarDocumentos],
  );

  const borrarDocumento = useCallback(
    async (id: string) => {
      await api.borrarDocumento(id);
      await refrescarDocumentos();
    },
    [refrescarDocumentos],
  );

  const crearFolder = useCallback(
    async (nombre: string) => {
      if (dominioActivo === "todas") return;
      await api.crearFolder(nombre, dominioActivo);
      await refrescarDocumentos();
    },
    [dominioActivo, refrescarDocumentos],
  );

  const borrarFolder = useCallback(
    async (id: string) => {
      await api.borrarFolder(id);
      await refrescarDocumentos();
    },
    [refrescarDocumentos],
  );

  const abrirConversacion = useCallback(async (id: string) => {
    const conv = await api.listarConversaciones().then((cs) => cs.find((c) => c.id === id) ?? null);
    const mensajes = await api.mensajesDe(id);
    setConversacionActiva(conv);
    setChat({ mensajes, enviando: false, error: null, actividad: [], metricas: null });
    setFuentesSeleccionadas(null);
  }, []);

  const nuevaConversacion = useCallback(async () => {
    setConversacionActiva(null);
    setChat({ mensajes: [], enviando: false, error: null, actividad: [], metricas: null });
    setFuentesSeleccionadas(null);
  }, []);

  const borrarConversacion = useCallback(
    async (id: string) => {
      await api.borrarConversacion(id);
      if (conversacionActiva?.id === id) await nuevaConversacion();
      await refrescarConversaciones();
    },
    [conversacionActiva, nuevaConversacion, refrescarConversaciones],
  );

  const preguntar = useCallback(
    async (texto: string, modelo?: string, razonamiento?: string, perfil: PerfilChat = "normal") => {
      detenidoRef.current = false;
      let convId = conversacionActiva?.id ?? null;
      if (!convId) {
        const creada = await api.crearConversacion(dominioActivo === "todas" ? [] : [dominioActivo]);
        convId = creada.id;
        setConversacionActiva(creada);
      }

      const mensajeUsuario: MensajeChat = { id: idTemporal(), rol: "user", contenido: texto };
      const borrador: MensajeChat = { id: idTemporal(), rol: "assistant", contenido: "", pendiente: true };
      setChat((c) => ({
        ...c,
        enviando: true,
        error: null,
        actividad: [],
        metricas: null,
        mensajes: [...c.mensajes, mensajeUsuario, borrador],
      }));

      abortRef.current?.abort();
      abortRef.current = new AbortController();

      const actualizarBorrador = (fn: (m: MensajeChat) => MensajeChat) =>
        setChat((c) => ({
          ...c,
          mensajes: c.mensajes.map((m) => (m.id === borrador.id ? fn(m) : m)),
        }));

      let streamFinalizadoConError = false;
      let respuestaCompletada = false;
      try {
        await streamChat(
          `/api/conversations/${convId}/messages`,
          { pregunta: texto, modelo, razonamiento, perfil },
          {
            onAgent(progreso) {
              const paso = describirAgente(progreso);
              if (paso) setChat((c) => ({ ...c, actividad: [...c.actividad, paso] }));
            },
            onToken(t) {
              actualizarBorrador((m) => ({ ...m, contenido: m.contenido + t }));
            },
            onProgress(progreso) {
              setChat((c) => ({ ...c, actividad: [...c.actividad, progreso.texto] }));
            },
            onDone({ content, sources, trace, metrics }) {
              respuestaCompletada = true;
              setChat((c) => ({ ...c, actividad: [], metricas: metrics ?? null }));
              actualizarBorrador((m) => ({
                ...m,
                contenido: content || m.contenido,
                fuentes: sources,
                traza: trace as TrazaPipeline,
                metricas: metrics ?? null,
                pendiente: false,
              }));
            },
            onVerified(datos) {
              const verificacion = datos as unknown as Verificacion & { revision?: string };
              actualizarBorrador((m) => ({
                ...m,
                verificacion: { verdict: verificacion.verdict, critique: (verificacion as any).critique },
                revisionContenido: verificacion.revision ?? m.revisionContenido,
              }));
            },
            onRevisionAvailable(datos) {
              actualizarBorrador((m) => ({ ...m, revisionContenido: datos.revision }));
            },
            onError(err) {
              if (respuestaCompletada) return;
              streamFinalizadoConError = true;
              setChat((c) => ({
                ...c,
                error: err.message,
                actividad: [],
                mensajes: c.mensajes.filter((m) => m.id !== borrador.id),
              }));
            },
          },
          abortRef.current.signal,
        );
      } catch (error) {
        if (detenidoRef.current || (error instanceof DOMException && error.name === "AbortError")) return;
        throw error;
      } finally {
        setChat((c) => ({ ...c, enviando: false }));
        void refrescarConversaciones();
        // Una respuesta fallida no se persiste en el backend. No recargarla aquí
        // para no borrar el mensaje temporal y el error que acaba de mostrar la UI.
        if (!detenidoRef.current && !streamFinalizadoConError) {
          const mensajes = await api.mensajesDe(convId).catch(() => null);
          if (mensajes) setChat((c) => ({ ...c, mensajes }));
        }
      }
    },
    [conversacionActiva, dominioActivo, refrescarConversaciones],
  );

  const aceptarRevision = useCallback(
    async (messageId: string) => {
      if (!conversacionActiva) return;
      const resultado = await api.aceptarRevision(conversacionActiva.id, messageId);
      setChat((c) => ({
        ...c,
        mensajes: c.mensajes.map((m) =>
          m.id === messageId
            ? { ...m, contenido: resultado.content, revisionContenido: null }
            : m,
        ),
      }));
    },
    [conversacionActiva],
  );

  const entrarApp = useCallback(() => setVista("app"), []);

  const value = useMemo<AppContextValue>(
    () => ({
      vista,
      entrarApp,
      tema,
      alternarTema,
      dominioActivo,
      setDominioActivo,
      documentos,
      folders,
      refrescarDocumentos,
      subirDocumento,
      borrarDocumento,
      crearFolder,
      borrarFolder,
      subidaActiva,
      docResaltado,
      errorSubida,
      conversaciones,
      conversacionActiva,
      chat,
      fuentesSeleccionadas,
      seleccionarFuentes: setFuentesSeleccionadas,
      refrescarConversaciones,
      abrirConversacion,
      nuevaConversacion,
      borrarConversacion,
      preguntar,
      detenerGeneracion,
      aceptarRevision,
    }),
    [
      vista, entrarApp, tema, alternarTema, dominioActivo, documentos, folders, refrescarDocumentos,
      subirDocumento, borrarDocumento, crearFolder, borrarFolder, subidaActiva, docResaltado,
      errorSubida, conversaciones, conversacionActiva,
      chat, fuentesSeleccionadas, refrescarConversaciones, abrirConversacion, nuevaConversacion,
       borrarConversacion, preguntar, detenerGeneracion, aceptarRevision,
    ],
  );

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>;
}
