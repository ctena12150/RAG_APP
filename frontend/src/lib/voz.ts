import { useCallback, useEffect, useRef, useState } from "react";
import type { MensajeChat } from "./types";

/* Utilidades de voz en el navegador (Web Speech API):
   - Lectura en voz alta (TTS) de una conversación completa.
   - Dictado por voz (reconocimiento de voz → texto) para el composer del chat.
   Todo corre en el cliente: no se envía audio ni texto a ningún servicio del backend. */

/* ============================ Lectura en voz alta ============================ */

export function soportaSintesis(): boolean {
  return typeof window !== "undefined" && "speechSynthesis" in window;
}

/** Deja el texto "pronunciable": sin citas (Fuente N), código ni sintaxis markdown. */
export function limpiarTextoParaVoz(texto: string): string {
  return texto
    .replace(/```[\s\S]*?```/g, " ")
    .replace(/`([^`]*)`/g, "$1")
    .replace(/!?\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/^\s{0,3}#{1,6}\s+/gm, "")
    .replace(/\*\*([^*]+)\*\*/g, "$1")
    .replace(/\*([^*]+)\*/g, "$1")
    .replace(/\s*\(Fuente\s+\d{1,2}\)/g, "")
    .replace(/[ \t]+/g, " ")
    .trim();
}

/** Compone el guion de la conversación: "Pregunta: … Respuesta: …" por turno. */
export function componerTextoConversacion(mensajes: MensajeChat[]): string {
  const turnos: string[] = [];
  for (const mensaje of mensajes) {
    const contenido = limpiarTextoParaVoz(mensaje.contenido);
    if (!contenido) continue;
    turnos.push(mensaje.rol === "user" ? `Pregunta: ${contenido}` : `Respuesta: ${contenido}`);
  }
  return turnos.join(" ");
}

/** Texto plano de un mensaje para el portapapeles: sin citas (Fuente N) ni markdown. */
export function textoPlanoMarkdown(texto: string): string {
  return limpiarTextoParaVoz(texto);
}

let vozEspanol: SpeechSynthesisVoice | null | undefined;

function elegirVoz(): SpeechSynthesisVoice | null {
  if (!soportaSintesis()) return null;
  if (vozEspanol !== undefined) return vozEspanol;
  const voces = window.speechSynthesis.getVoices();
  vozEspanol =
    voces.find((v) => v.lang?.toLowerCase().startsWith("es-es")) ??
    voces.find((v) => v.lang?.toLowerCase().startsWith("es")) ??
    null;
  return vozEspanol;
}

const MAX_CARACTERES_UTERANCIA = 220;

/** Trocea por frases: las uterancias muy largas se cortan en algunos motores de voz. */
function trocear(guion: string): string[] {
  const frases = guion.match(/[^.!?…]+[.!?…]*\s*/g) ?? [guion];
  const trozos: string[] = [];
  let actual = "";
  for (const frase of frases) {
    if (frase.length > MAX_CARACTERES_UTERANCIA) {
      if (actual.trim()) trozos.push(actual.trim());
      actual = "";
      for (let i = 0; i < frase.length; i += MAX_CARACTERES_UTERANCIA) {
        const trozo = frase.slice(i, i + MAX_CARACTERES_UTERANCIA).trim();
        if (trozo) trozos.push(trozo);
      }
      continue;
    }
    if (actual && (actual + frase).length > MAX_CARACTERES_UTERANCIA) {
      trozos.push(actual.trim());
      actual = "";
    }
    actual += frase;
  }
  if (actual.trim()) trozos.push(actual.trim());
  return trozos.filter(Boolean);
}

/**
 * Lee un texto en voz alta con una voz en español si existe.
 * Cancela cualquier lectura en curso; llama a onFin al terminar (también si el
 * motor falla; el llamador decide si el estado sigue siendo válido).
 */
export function leerEnVozAlta(id: string, texto: string, onFin?: () => void): void {
  if (!soportaSintesis()) {
    onFin?.();
    return;
  }
  detenerLectura();
  const guion = limpiarTextoParaVoz(texto);
  if (!guion) {
    onFin?.();
    return;
  }
  marcarLector(id);
  const sintesis = window.speechSynthesis;
  const voz = elegirVoz();
  const trozos = trocear(guion);
  let pendientes = trozos.length;
  const terminar = () => {
    pendientes -= 1;
    if (pendientes === 0) {
      marcarLector(null);
      onFin?.();
    }
  };
  for (const trozo of trozos) {
    const utterance = new SpeechSynthesisUtterance(trozo);
    utterance.lang = "es-ES";
    if (voz) utterance.voice = voz;
    utterance.onend = terminar;
    utterance.onerror = terminar;
    sintesis.speak(utterance);
  }
}

/** Detiene la lectura en curso (si la hubiera). */
export function detenerLectura(): void {
  if (!soportaSintesis()) return;
  window.speechSynthesis.cancel();
  marcarLector(null);
}

/* Registro global de lectura activa: permite a varios mensajes sincronizar su
   botón Escuchar/Detener cuando la voz se inicia o corta desde otro mensaje. */
let lectorActual: string | null = null;
const oyentesLectura = new Set<(id: string | null) => void>();

/** Id del elemento que se está leyendo ahora mismo (null = en silencio). */
export function idLecturaActual(): string | null {
  return lectorActual;
}

/** Suscribe un callback a los cambios de lectura activa; devuelve la cancelación. */
export function suscribirLectura(cb: (id: string | null) => void): () => void {
  oyentesLectura.add(cb);
  return () => oyentesLectura.delete(cb);
}

function marcarLector(id: string | null): void {
  lectorActual = id;
  for (const oyente of oyentesLectura) oyente(id);
}

/* ============================ Dictado por voz ============================ */

type ConstructorReconocimiento = new () => ReconocimientoVoz;

/** Contrato mínimo de SpeechRecognition que usa el dictado (no depende de lib.dom). */
interface ReconocimientoVoz extends EventTarget {
  lang: string;
  continuous: boolean;
  interimResults: boolean;
  start(): void;
  stop(): void;
  abort(): void;
  onresult: ((evento: EventoResultadoVoz) => void) | null;
  onend: (() => void) | null;
  onerror: ((evento: Event & { error?: string }) => void) | null;
}

interface EventoResultadoVoz extends Event {
  resultIndex: number;
  results: ArrayLike<{ isFinal: boolean; [indice: number]: { transcript: string } }>;
}

function constructorDictado(): ConstructorReconocimiento | null {
  if (typeof window === "undefined") return null;
  const w = window as unknown as {
    SpeechRecognition?: ConstructorReconocimiento;
    webkitSpeechRecognition?: ConstructorReconocimiento;
  };
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null;
}

/** El dictado solo existe en navegadores con Web Speech API de reconocimiento (Chrome/Edge). */
export function soportaDictado(): boolean {
  return constructorDictado() !== null;
}

export interface OpcionesDictado {
  /** Recibe cada fragmento reconocido (solo resultados finales, ya recortados). */
  onTranscribir: (texto: string) => void;
}

/**
 * Hook de dictado: convierte la voz del micrófono en texto en español.
 * Entrega solo fragmentos finales (los parciales se descartan para no duplicar).
 */
export function useDictado({ onTranscribir }: OpcionesDictado) {
  const [grabando, setGrabando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const reconocimientoRef = useRef<ReconocimientoVoz | null>(null);
  const onTranscribirRef = useRef(onTranscribir);

  useEffect(() => {
    onTranscribirRef.current = onTranscribir;
  }, [onTranscribir]);

  const detener = useCallback(() => {
    reconocimientoRef.current?.stop();
  }, []);

  const iniciar = useCallback(() => {
    const Constructor = constructorDictado();
    if (!Constructor) return;
    if (reconocimientoRef.current) {
      reconocimientoRef.current.abort(); // nunca dos sesiones a la vez
      reconocimientoRef.current = null;
    }
    setError(null);
    const reconocimiento = new Constructor();
    reconocimiento.lang = "es-ES";
    reconocimiento.continuous = true;
    reconocimiento.interimResults = true;
    reconocimiento.onresult = (evento) => {
      let fragmento = "";
      for (let i = evento.resultIndex; i < evento.results.length; i++) {
        const resultado = evento.results[i];
        if (resultado.isFinal) fragmento += resultado[0]?.transcript ?? "";
      }
      const limpio = fragmento.trim();
      if (limpio) onTranscribirRef.current(limpio);
    };
    reconocimiento.onerror = (evento) => {
      if (evento.error === "not-allowed" || evento.error === "service-not-allowed") {
        setError("El micrófono está bloqueado. Permite el acceso para poder dictar.");
      } else if (evento.error === "no-speech") {
        setError("No se ha detectado voz. Inténtalo de nuevo.");
      } else if (evento.error !== "aborted") {
        setError("El dictado no está disponible en este momento.");
      }
    };
    reconocimiento.onend = () => {
      setGrabando(false);
      reconocimientoRef.current = null;
    };
    reconocimientoRef.current = reconocimiento;
    try {
      reconocimiento.start();
      setGrabando(true);
    } catch {
      reconocimientoRef.current = null;
      setGrabando(false);
      setError("No se pudo iniciar el dictado.");
    }
  }, []);

  // al desmontar aborta la sesión para no dejar el micrófono abierto
  useEffect(() => () => reconocimientoRef.current?.abort(), []);

  return { grabando, error, iniciar, detener };
}


/** Copia texto al portapapeles; usa la API moderna y cae a un metodo legado si no esta disponible. */
export async function copiarAlPortapapeles(texto: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(texto);
      return true;
    }
  } catch {
    /* cae al metodo legado */
  }
  try {
    const area = document.createElement("textarea");
    area.value = texto;
    area.setAttribute("readonly", "");
    area.style.position = "fixed";
    area.style.opacity = "0";
    document.body.appendChild(area);
    area.select();
    const ok = document.execCommand("copy");
    area.remove();
    return ok;
  } catch {
    return false;
  }
}
