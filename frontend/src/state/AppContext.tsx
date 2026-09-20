import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import { api, describirAgente, streamChat } from "../lib/api";
import type { PerfilChat } from "../lib/api";
import type { Conversacion, Dominio, DominioInfo, Documento, Folder, Fuente, MensajeChat, MetricasGeneracion, Rol, TrazaPipeline, Usuario, UsuarioAdmin, UsuarioPermitido, Verificacion } from "../lib/types";

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

  usuario: Usuario | null;
  proveedoresDisponibles: string[];
  authCargando: boolean;
  iniciarSesion: (proveedor: string) => void;
  iniciarSesionLocal: (usuario: string, contrasena: string) => Promise<void>;
  cerrarSesion: () => void;

  /** El usuario autenticado puede subir/gestionar documentos (dev sin auth = siempre true). */
  puedeGestionarDocumentos: boolean;
  esSuperUsuario: boolean;

  /**
   * Dominios que el usuario puede gestionar. Null = todos (sin auth, superusuario
   * o teamleader sin restricción). Lista = solo esos dominios.
   */
  dominiosGestionables: Dominio[] | null;
  puedeGestionarDominio: (d: Dominio | "todas") => boolean;

  // administración de usuarios locales (solo superusuario)
  listarUsuarios: () => Promise<UsuarioAdmin[]>;
  crearUsuario: (datos: {
    usuario: string;
    contrasena: string;
    rol: Rol;
    email?: string | null;
    nombre?: string | null;
    dominios?: Dominio[] | null;
  }) => Promise<UsuarioAdmin>;
  actualizarUsuario: (id: string, cambios: {
    rol?: Rol;
    activo?: boolean;
    email?: string | null;
    nombre?: string | null;
    contrasena?: string;
    dominios?: Dominio[] | null;
  }) => Promise<UsuarioAdmin>;
  borrarUsuario: (id: string) => Promise<void>;

  // lista blanca de acceso Google (solo superusuario)
  listarPermitidos: () => Promise<UsuarioPermitido[]>;
  crearPermitido: (datos: { email?: string | null; dominio?: string | null }) => Promise<UsuarioPermitido>;
  actualizarPermitido: (id: string, cambios: { activo?: boolean }) => Promise<UsuarioPermitido>;
  borrarPermitido: (id: string) => Promise<void>;

  // catálogo de dominios (fuente de verdad: GET /api/dominios)
  dominios: DominioInfo[];
  etiquetaDominio: (clave: Dominio) => string;
  refrescarDominios: () => Promise<void>;
  listarDominios: () => Promise<DominioInfo[]>;
  crearDominio: (datos: { clave: string; etiqueta: string; descripcion?: string | null; ejemplos?: string[] }) => Promise<DominioInfo>;
  actualizarDominio: (clave: string, cambios: { etiqueta?: string; descripcion?: string | null; ejemplos?: string[] }) => Promise<DominioInfo>;
  borrarDominio: (clave: string) => Promise<void>;

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
  preguntar: (texto: string, modelo?: string, razonamiento?: string, perfil?: PerfilChat, dominios?: Dominio[]) => Promise<void>;
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
  const [usuario, setUsuario] = useState<Usuario | null>(null);
  const [proveedoresDisponibles, setProveedoresDisponibles] = useState<string[]>([]);
  const [authCargando, setAuthCargando] = useState(true);
  const [dominioActivo, setDominioActivo] = useState<Dominio | "todas">("rrhh");
  const [dominios, setDominios] = useState<DominioInfo[]>([]);
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
  // id de la conversación visible (ref para comparar en callbacks async sin cierres rancios)
  const convActivaRef = useRef<string | null>(null);
  const aperturaRef = useRef(0);

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

  // sesión al arrancar + expulsión si un 401 indica sesión caducada/usuario fuera de lista
  useEffect(() => {
    let activo = true;
    const chequearSesion = async () => {
      try {
        const estado = await api.me();
        if (!activo) return;
        setProveedoresDisponibles(estado.proveedores);
        if (estado.autenticado && estado.email) {
          setUsuario({ email: estado.email, nombre: estado.nombre, proveedor: estado.proveedor, rol: estado.rol, dominios: estado.dominios });
        } else {
          setUsuario(null);
          if (estado.proveedores.length > 0) setVista("landing");
        }
      } catch {
        if (activo) {
          setUsuario(null);
          setProveedoresDisponibles([]);
        }
      } finally {
        if (activo) setAuthCargando(false);
      }
    };
    const expulsar = () => {
      setUsuario(null);
      setAuthCargando(false);
      setVista("landing");
    };
    void chequearSesion();
    window.addEventListener("rag:no-autorizado", expulsar);
    return () => {
      activo = false;
      window.removeEventListener("rag:no-autorizado", expulsar);
    };
  }, []);

  const iniciarSesion = useCallback((proveedor: string) => api.iniciarSesion(proveedor), []);
  const iniciarSesionLocal = useCallback(async (usuario: string, contrasena: string) => {
    const estado = await api.iniciarSesionLocal(usuario, contrasena);
    setProveedoresDisponibles(estado.proveedores);
    if (estado.autenticado && estado.email) {
      setUsuario({ email: estado.email, nombre: estado.nombre, proveedor: estado.proveedor, rol: estado.rol, dominios: estado.dominios });
    }
  }, []);
  const cerrarSesion = useCallback(() => api.cerrarSesion(), []);

  const listarUsuarios = useCallback(() => api.listarUsuarios(), []);
  const crearUsuario = useCallback((datos: Parameters<typeof api.crearUsuario>[0]) => api.crearUsuario(datos), []);
  const actualizarUsuario = useCallback(
    (id: string, cambios: Parameters<typeof api.actualizarUsuario>[1]) => api.actualizarUsuario(id, cambios),
    [],
  );
  const borrarUsuario = useCallback((id: string) => api.borrarUsuario(id), []);

  const listarPermitidos = useCallback(() => api.listarPermitidos(), []);
  const crearPermitido = useCallback(
    (datos: Parameters<typeof api.crearPermitido>[0]) => api.crearPermitido(datos),
    [],
  );
  const actualizarPermitido = useCallback(
    (id: string, cambios: Parameters<typeof api.actualizarPermitido>[1]) => api.actualizarPermitido(id, cambios),
    [],
  );
  const borrarPermitido = useCallback((id: string) => api.borrarPermitido(id), []);

  const refrescarDominios = useCallback(async () => {
    try {
      const lista = await api.listarDominios();
      setDominios(lista);
      setDominioActivo((actual) => {
        if (actual === "todas") return actual;
        return lista.some((d) => d.clave === actual) ? actual : (lista[0]?.clave ?? actual);
      });
    } catch {
      /* el panel muestra estado vacío */
    }
  }, []);
  const listarDominios = useCallback(() => api.listarDominios(), []);
  const crearDominio = useCallback(async (datos: Parameters<typeof api.crearDominio>[0]) => {
    const creado = await api.crearDominio(datos);
    await refrescarDominios();
    return creado;
  }, [refrescarDominios]);
  const actualizarDominio = useCallback(async (clave: string, cambios: Parameters<typeof api.actualizarDominio>[1]) => {
    const actualizado = await api.actualizarDominio(clave, cambios);
    await refrescarDominios();
    return actualizado;
  }, [refrescarDominios]);
  const borrarDominio = useCallback(async (clave: string) => {
    await api.borrarDominio(clave);
    await refrescarDominios();
  }, [refrescarDominios]);
  const etiquetaDominio = useCallback(
    (clave: Dominio) => dominios.find((d) => d.clave === clave)?.etiqueta ?? clave,
    [dominios],
  );

  useEffect(() => {
    void refrescarDominios();
  }, [refrescarDominios]);

  // sin proveedores configurados = modo dev sin auth: acceso completo (como hoy)
  const puedeGestionarDocumentos =
    proveedoresDisponibles.length === 0 || usuario?.rol === "teamleader" || usuario?.rol === "superusuario";
  const esSuperUsuario = usuario?.rol === "superusuario";

  // null = todos los dominios (dev sin auth, superusuario o teamleader sin restricción)
  const dominiosGestionables: Dominio[] | null =
    proveedoresDisponibles.length === 0 || usuario?.rol === "superusuario" || !usuario?.dominios
      ? null
      : usuario.dominios;
  const puedeGestionarDominio = useCallback(
    (d: Dominio | "todas") =>
      !puedeGestionarDocumentos ? false : dominiosGestionables === null || d === "todas" ? true : dominiosGestionables.includes(d),
    [puedeGestionarDocumentos, dominiosGestionables],
  );

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
    const turno = ++aperturaRef.current;
    const conv = conversaciones.find((c) => c.id === id) ?? null;
    const mensajes = await api.mensajesDe(id);
    if (turno !== aperturaRef.current) return;
    convActivaRef.current = id;
    setConversacionActiva(conv);
    setChat({ mensajes, enviando: false, error: null, actividad: [], metricas: null });
    setFuentesSeleccionadas(null);
  }, [conversaciones]);

  const nuevaConversacion = useCallback(async () => {
    aperturaRef.current++;
    convActivaRef.current = null;
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
    async (texto: string, modelo?: string, razonamiento?: string, perfil: PerfilChat = "normal", dominios?: Dominio[]) => {
      detenidoRef.current = false;
      let convId = conversacionActiva?.id ?? null;
      if (!convId) {
        const creada = await api.crearConversacion(
          dominios && dominios.length > 0 ? dominios : dominioActivo === "todas" ? [] : [dominioActivo]);
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
      const controller = new AbortController();
      abortRef.current = controller;
      convActivaRef.current = convId;

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
          { pregunta: texto, modelo, razonamiento, perfil, dominios: dominios && dominios.length > 0 ? dominios : undefined },
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
            onDone({ content, sources, trace, metrics, clarify }) {
              respuestaCompletada = true;
              setChat((c) => ({ ...c, actividad: [], metricas: metrics ?? null }));
              const opciones = Array.isArray(clarify?.opciones)
                ? clarify.opciones
                    .filter((o) => o && typeof o.texto === "string" && typeof o.valor === "string")
                    .map((o) => ({ texto: o.texto.trim().slice(0, 80), valor: o.valor.trim().slice(0, 200) }))
                    .filter((o) => o.texto && o.valor)
                : null;
              actualizarBorrador((m) => ({
                ...m,
                contenido: content || m.contenido,
                fuentes: sources,
                traza: trace as TrazaPipeline,
                metricas: metrics ?? null,
                opcionesAclaracion: opciones && opciones.length > 0 ? opciones : null,
                pendiente: false,
              }));
            },
            onVerified(datos) {
              const verificacion: Verificacion =
                datos.verdict === "supported"
                  ? { verdict: "supported" }
                  : datos.verdict === "error"
                    ? { verdict: "error" }
                    : { verdict: "unsupported", critique: datos.critique, revision: datos.revision };
              actualizarBorrador((m) => ({
                ...m,
                verificacion,
                revisionContenido: datos.revision ?? m.revisionContenido,
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
          controller.signal,
        );
      } catch (error) {
        if (detenidoRef.current || (error instanceof DOMException && error.name === "AbortError")) return;
        // fallo de red antes del primer evento: streamChat nunca llamó onError
        if (abortRef.current === controller && !respuestaCompletada) {
          streamFinalizadoConError = true;
          const mensaje = error instanceof Error ? error.message : "Error de conexión.";
          setChat((c) => ({
            ...c,
            error: mensaje,
            actividad: [],
            mensajes: c.mensajes.filter((m) => m.id !== borrador.id),
          }));
        }
        return;
      } finally {
        // el stream abortado/detenido no toca el estado del stream vigente
        if (abortRef.current !== controller) return;
        setChat((c) => ({ ...c, enviando: false }));
        void refrescarConversaciones();
        // Una respuesta fallida no se persiste en el backend. No recargarla aquí
        // para no borrar el mensaje temporal y el error que acaba de mostrar la UI.
        // Y si el usuario ya cambió de conversación, no pisar la vista nueva.
        if (!detenidoRef.current && !streamFinalizadoConError && convActivaRef.current === convId) {
          const mensajes = await api.mensajesDe(convId).catch(() => null);
          if (mensajes && abortRef.current === controller && convActivaRef.current === convId)
            setChat((c) => ({ ...c, mensajes }));
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
      usuario,
      proveedoresDisponibles,
      authCargando,
      iniciarSesion,
      iniciarSesionLocal,
      cerrarSesion,
      puedeGestionarDocumentos,
      esSuperUsuario,
      dominiosGestionables,
      puedeGestionarDominio,
      listarUsuarios,
      crearUsuario,
      actualizarUsuario,
      borrarUsuario,
      listarPermitidos,
      crearPermitido,
      actualizarPermitido,
      borrarPermitido,
      dominios,
      etiquetaDominio,
      refrescarDominios,
      listarDominios,
      crearDominio,
      actualizarDominio,
      borrarDominio,
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
      vista, entrarApp, tema, alternarTema, usuario, proveedoresDisponibles, authCargando,
      iniciarSesion, iniciarSesionLocal, cerrarSesion, puedeGestionarDocumentos, esSuperUsuario,
      dominiosGestionables, puedeGestionarDominio,
      listarUsuarios, crearUsuario, actualizarUsuario, borrarUsuario,
      listarPermitidos, crearPermitido, actualizarPermitido, borrarPermitido,
      dominios, etiquetaDominio, refrescarDominios, listarDominios, crearDominio, actualizarDominio, borrarDominio,
      dominioActivo, documentos, folders, refrescarDocumentos,
      subirDocumento, borrarDocumento, crearFolder, borrarFolder, subidaActiva, docResaltado,
      errorSubida, conversaciones, conversacionActiva,
      chat, fuentesSeleccionadas, refrescarConversaciones, abrirConversacion, nuevaConversacion,
       borrarConversacion, preguntar, detenerGeneracion, aceptarRevision,
    ],
  );

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>;
}
