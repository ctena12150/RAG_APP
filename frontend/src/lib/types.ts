export type Dominio = "rrhh" | "mantenimiento" | "onboarding";

export interface ModeloDisponible {
  proveedor: "ollama" | "groq";
  modelo: string;
  nombre: string;
  local: boolean;
  seleccionable: boolean;
  capacidades: string[];
}

export const DOMINIOS: Dominio[] = ["rrhh", "mantenimiento", "onboarding"];

export const ETIQUETA_DOMINIO: Record<Dominio, string> = {
  rrhh: "Recursos Humanos",
  mantenimiento: "Mantenimiento",
  onboarding: "Onboarding",
};

export type EstadoDocumento = "pendiente" | "procesando" | "listo" | "error";

export interface Documento {
  id: string;
  nombreArchivo: string;
  dominio: Dominio;
  folderId: string | null;
  tamanoBytes: number;
  estado: EstadoDocumento;
  errorMensaje?: string | null;
  totalPaginas?: number | null;
  creadoUtc: string;
}

export interface Folder {
  id: string;
  nombre: string;
  dominio: Dominio;
}

export interface Fuente {
  indice: number;
  documentoId: string;
  documentoNombre: string;
  chunkId: string;
  chunkIndice: number;
  pagina?: number | null;
  seccion?: string | null;
  fragmento: string;
  puntuacion: number;
  usada: boolean;
}

export interface EtapaTraza {
  etapa: string;
  duracionMs: number;
  detalle?: Record<string, unknown>;
}

export interface TrazaPipeline {
  modo: "fijo" | "agentico";
  etapas: EtapaTraza[];
}

export interface MetricasGeneracion {
  tokens: number;
  tokensEstimados?: boolean;
  generacionMs: number;
  totalMs: number;
  modelo?: string | null;
  proveedor?: string | null;
  fallback?: boolean;
  modeloSolicitado?: string | null;
  proveedorSolicitado?: string | null;
  razonamiento?: string | null;
}

export type NivelRazonamiento = "off" | "low" | "medium" | "high";

export type Verificacion =
  | { verdict: "supported" }
  | { verdict: "unsupported"; critique?: string }
  | { verdict: "error" };

export interface MensajeChat {
  id: string;
  rol: "user" | "assistant";
  contenido: string;
  fuentes?: Fuente[] | null;
  traza?: TrazaPipeline | null;
  verificacion?: Verificacion | null;
  revisionContenido?: string | null;
  pendiente?: boolean;
  cancelada?: boolean;
  metricas?: MetricasGeneracion | null;
}

export interface Conversacion {
  id: string;
  titulo: string;
  tituloAutomatico: boolean;
  dominios: Dominio[];
  documentosIds?: string[] | null;
  creadoUtc: string;
}
