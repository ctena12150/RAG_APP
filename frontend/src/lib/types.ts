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
  | { verdict: "unsupported"; critique?: string; revision?: string }
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

export type ProveedorLogin = "google" | "local";

export type Rol = "usuario" | "teamleader" | "superusuario";

export const ROLES: Rol[] = ["usuario", "teamleader", "superusuario"];

export const ETIQUETA_ROL: Record<Rol, string> = {
  usuario: "Usuario",
  teamleader: "Team Leader",
  superusuario: "Superusuario",
};

export interface EstadoAuth {
  autenticado: boolean;
  email: string | null;
  nombre: string | null;
  proveedor: string | null;
  rol: Rol | null;
  /** Dominios gestionables (teamleader). Null = sin restricción (superusuario o todos). */
  dominios: Dominio[] | null;
  proveedores: ProveedorLogin[];
}

export interface Usuario {
  email: string;
  nombre?: string | null;
  proveedor?: string | null;
  rol?: Rol | null;
  dominios?: Dominio[] | null;
}

/** Entrada de la lista blanca de acceso Google: email exacto o dominio (nunca ambos). */
export interface UsuarioPermitido {
  id: string;
  email?: string | null;
  dominio?: string | null;
  activo: boolean;
  creadoUtc: string;
}

/** Usuario local gestionado por el superusuario (formulario de administración). */
export interface UsuarioAdmin {
  id: string;
  usuario: string;
  email?: string | null;
  nombre?: string | null;
  rol: Rol;
  dominios: Dominio[];
  activo: boolean;
  creadoUtc: string;
}
