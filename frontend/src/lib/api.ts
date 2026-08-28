import type { Conversacion, Documento, Dominio, Folder, Fuente, MensajeChat, MetricasGeneracion, ModeloDisponible, TrazaPipeline, Verificacion } from "./types";

const BASE = import.meta.env.VITE_API_BASE_URL ?? "";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const resp = await fetch(`${BASE}${path}`, init);
  if (!resp.ok) {
    let mensaje = `Error ${resp.status}`;
    try {
      const cuerpo = await resp.json();
      if (cuerpo?.error?.message) mensaje = cuerpo.error.message;
    } catch {
      /* cuerpo sin json */
    }
    throw new Error(mensaje);
  }
  return resp.status === 204 ? (undefined as T) : ((await resp.json()) as T);
}

type MensajeApi = MensajeChat & {
  fuentesJson?: string | Fuente[] | null;
  trazaJson?: string | TrazaPipeline | null;
  verificacionJson?: string | Verificacion | null;
  metricasJson?: string | MetricasGeneracion | null;
};

function parsearJson<T>(valor: unknown): T | null {
  if (valor === null || valor === undefined || valor === "") return null;
  if (typeof valor !== "string") return valor as T;
  try {
    return JSON.parse(valor) as T;
  } catch {
    return null;
  }
}

/** Convierte el contrato de persistencia .NET al contrato de vista del frontend. */
export function normalizarMensaje(mensaje: MensajeApi): MensajeChat {
  return {
    id: mensaje.id,
    rol: mensaje.rol,
    contenido: mensaje.contenido,
    fuentes: mensaje.fuentes ?? parsearJson<Fuente[]>(mensaje.fuentesJson),
    traza: mensaje.traza ?? parsearJson<TrazaPipeline>(mensaje.trazaJson),
    verificacion: mensaje.verificacion ?? parsearJson<Verificacion>(mensaje.verificacionJson),
    metricas: mensaje.metricas ?? parsearJson<MetricasGeneracion>(mensaje.metricasJson),
    revisionContenido: mensaje.revisionContenido,
    pendiente: mensaje.pendiente,
  };
}

/** Opciones de subida: carpeta destino y callback de progreso del envío (0..1). */
export interface OpcionesSubida {
  folderId?: string | null;
  onProgresoSubida?: (progreso: number) => void;
}

export const api = {
  // XMLHttpRequest en vez de fetch: es la única forma fiable de reportar el % de envío
  subirDocumento(file: File, dominio: Dominio, opciones?: OpcionesSubida): Promise<Documento> {
    return new Promise<Documento>((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open("POST", `${BASE}/api/documents/upload`);
      xhr.responseType = "json";
      xhr.upload.onprogress = (e) => {
        if (e.lengthComputable && e.total > 0) {
          opciones?.onProgresoSubida?.(Math.min(1, e.loaded / e.total));
        }
      };
      xhr.onerror = () => reject(new Error("No se pudo conectar con el servidor."));
      xhr.onload = () => {
        const cuerpo = xhr.response as { error?: { message?: string } } | null;
        if (xhr.status >= 200 && xhr.status < 300 && cuerpo && typeof cuerpo === "object" && "id" in cuerpo) {
          resolve(cuerpo as unknown as Documento);
          return;
        }
        reject(new Error(cuerpo?.error?.message ?? `Error ${xhr.status}`));
      };
      const form = new FormData();
      form.append("file", file);
      form.append("dominio", dominio);
      if (opciones?.folderId) form.append("folderId", opciones.folderId);
      xhr.send(form);
    });
  },

  listarDocumentos(filtros?: { dominio?: Dominio; q?: string }): Promise<Documento[]> {
    const params = new URLSearchParams();
    if (filtros?.dominio) params.set("dominio", filtros.dominio);
    if (filtros?.q) params.set("q", filtros.q);
    return request<Documento[]>(`/api/documents?${params}`);
  },

  estadoDocumento(id: string): Promise<{ estado: string; errorMensaje?: string | null }> {
    return request(`/api/documents/${id}/status`);
  },

  borrarDocumento(id: string): Promise<void> {
    return request<void>(`/api/documents/${id}`, { method: "DELETE" });
  },

  listarFolders(dominio?: Dominio): Promise<Folder[]> {
    const qs = dominio ? `?dominio=${dominio}` : "";
    return request<Folder[]>(`/api/folders${qs}`);
  },

  crearFolder(nombre: string, dominio: Dominio): Promise<Folder> {
    return request<Folder>("/api/folders", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ nombre, dominio }),
    });
  },

  borrarFolder(id: string): Promise<void> {
    return request<void>(`/api/folders/${id}`, { method: "DELETE" });
  },

  moverDocumentoAFolder(id: string, folderId: string | null): Promise<Documento> {
    return request<Documento>(`/api/documents/${id}/folder`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ folderId }),
    });
  },

  crearConversacion(dominios?: Dominio[]): Promise<Conversacion> {
    return request<Conversacion>("/api/conversations", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ dominios }),
    });
  },

  listarConversaciones(q?: string): Promise<Conversacion[]> {
    const qs = q ? `?q=${encodeURIComponent(q)}` : "";
    return request<Conversacion[]>(`/api/conversations${qs}`);
  },

  mensajesDe(conversacionId: string): Promise<MensajeChat[]> {
    return request<MensajeApi[]>(`/api/conversations/${conversacionId}/messages`).then((mensajes) =>
      mensajes.map(normalizarMensaje),
    );
  },

  borrarConversacion(id: string): Promise<void> {
    return request<void>(`/api/conversations/${id}`, { method: "DELETE" });
  },

  aceptarRevision(conversacionId: string, messageId: string): Promise<{ content: string }> {
    return request(`/api/conversations/${conversacionId}/messages/${messageId}/revision`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({}),
    });
  },

  health(): Promise<{ ragService: { disponible: boolean }; documentosListos: number }> {
    return request("/api/health");
  },

  listarModelos(): Promise<ModeloDisponible[]> {
    return request<{ modelos: ModeloDisponible[] }>("/api/models").then((respuesta) => respuesta.modelos);
  },
};

export interface ProgresoAgente {
  etapa: "planificacion" | "buscando" | "hallazgo";
  documentos?: number;
  agente?: string;
  query?: string;
  pasajes?: number;
  duracionMs?: number;
}

export interface SseHandlers {
  onMeta?(meta: { messageId: string; conversationId?: string | null }): void;
  onAgent?(progreso: ProgresoAgente): void;
  onProgress?(progreso: { etapa: string; texto: string }): void;
  onToken?(texto: string): void;
  onDone?(datos: { messageId: string; content: string; sources: MensajeChat["fuentes"]; trace: unknown; metrics?: MetricasGeneracion }): void;
  onVerified?(datos: Record<string, unknown>): void;
  onRevisionAvailable?(datos: { revision: string; critique?: string }): void;
  onError?(error: { code: string; message: string }): void;
}

export type PerfilChat = "normal" | "fast";

/** Consume el stream SSE del backend y reparte los eventos a los handlers. */
export async function streamChat(
  path: string,
  cuerpo: object,
  handlers: SseHandlers,
  signal?: AbortSignal,
): Promise<void> {
  const resp = await fetch(`${BASE}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(cuerpo),
    signal,
  });

  if (!resp.ok || !resp.body) {
    let mensaje = `Error ${resp.status}`;
    try {
      const c = await resp.json();
      if (c?.error?.message) mensaje = c.error.message;
    } catch {
      /* sin cuerpo */
    }
    handlers.onError?.({ code: String(resp.status), message: mensaje });
    return;
  }

  const reader = resp.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let eventoActual = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    let corte: number;
    while ((corte = buffer.indexOf("\n\n")) >= 0) {
      const bloque = buffer.slice(0, corte);
      buffer = buffer.slice(corte + 2);
      let datos: string | null = null;
      for (const linea of bloque.split("\n")) {
        if (linea.startsWith("event:")) eventoActual = linea.slice(6).trim();
        else if (linea.startsWith("data:")) datos = linea.slice(5).trim();
      }
      if (datos === null) continue;
      let payload: any = {};
      try {
        payload = JSON.parse(datos);
      } catch {
        payload = {};
      }
      switch (eventoActual) {
        case "meta":
          handlers.onMeta?.(payload);
          break;
        case "agent":
          handlers.onAgent?.(payload as ProgresoAgente);
          break;
        case "progress":
          handlers.onProgress?.(payload);
          break;
        case "token":
          handlers.onToken?.(payload.t ?? "");
          break;
        case "done":
          handlers.onDone?.(payload);
          break;
        case "verified":
          handlers.onVerified?.(payload);
          break;
        case "revision_available":
          handlers.onRevisionAvailable?.(payload);
          break;
        case "error":
          handlers.onError?.(payload);
          break;
      }
    }
  }
}

/** Traduce un evento "agent" del Director a una descripción legible para la UI. */
export function describirAgente(p: ProgresoAgente): string {
  switch (p.etapa) {
    case "planificacion":
      return `Director planificando (${p.documentos ?? 0} documentos)…`;
    case "buscando":
      return `${p.agente ?? "agente"}: "${p.query ?? ""}"`;
    case "hallazgo":
      return `${p.agente ?? "agente"}: ${p.pasajes ?? 0} pasajes`;
    default:
      return "";
  }
}

/** Extrae referencias (Fuente N) del texto para renderizar sellos de cita. */
export function dividirPorCitas(texto: string): Array<{ tipo: "texto" | "cita"; valor: string }> {
  const partes: Array<{ tipo: "texto" | "cita"; valor: string }> = [];
  const regex = /\(Fuente\s+(\d{1,2})\)/g;
  let ultimo = 0;
  let m: RegExpExecArray | null;
  while ((m = regex.exec(texto)) !== null) {
    if (m.index > ultimo) partes.push({ tipo: "texto", valor: texto.slice(ultimo, m.index) });
    partes.push({ tipo: "cita", valor: m[1] });
    ultimo = m.index + m[0].length;
  }
  if (ultimo < texto.length) partes.push({ tipo: "texto", valor: texto.slice(ultimo) });
  return partes;
}
