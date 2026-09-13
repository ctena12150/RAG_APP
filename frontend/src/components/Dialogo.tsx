import { useEffect } from "react";
import type { ReactNode } from "react";
import { motion, useReducedMotion } from "framer-motion";
import { X } from "lucide-react";

/**
 * Shell reutilizable de diálogo modal: overlay + tarjeta centrada con título,
 * subtítulo y cierre (Escape, click fuera o la X). Los formularios viven dentro.
 */
export default function Dialogo({
  titulo,
  subtitulo,
  onClose,
  children,
  ancho = "w-[480px]",
}: {
  titulo: string;
  subtitulo?: string;
  onClose: () => void;
  children: ReactNode;
  ancho?: string;
}) {
  const reduceMotion = useReducedMotion() ?? false;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center p-4"
      style={{ background: "var(--overlay)" }}
      onClick={onClose}
    >
      <motion.div
        role="dialog"
        aria-modal="true"
        aria-label={titulo}
        initial={reduceMotion ? { opacity: 1 } : { opacity: 0, y: 14, scale: 0.98 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: 0.25 }}
        className={`${ancho} max-w-[94vw] rounded-2xl p-6`}
        style={{
          background: "var(--bg-elev)",
          border: "1px solid var(--line)",
          color: "var(--ink)",
          boxShadow: "var(--shadow-xl)",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-5 flex items-start justify-between gap-4">
          <div className="text-left">
            <h2 className="text-lg font-semibold leading-tight">{titulo}</h2>
            {subtitulo && (
              <p className="text-xs" style={{ color: "var(--ink-soft)" }}>
                {subtitulo}
              </p>
            )}
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Cerrar"
            className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full border cursor-pointer transition hover:brightness-110"
            style={{ borderColor: "var(--line)", color: "var(--ink-soft)" }}
          >
            <X size={15} />
          </button>
        </div>
        {children}
      </motion.div>
    </div>
  );
}

/** Estilos compartidos de campos de formulario en diálogos. */
export const campoDialogo = "w-full rounded-md px-2 py-1.5 text-sm outline-none focus:ring-1";
export const estiloCampoDialogo = {
  background: "var(--bg)",
  border: "1px solid var(--line)",
  color: "var(--ink)",
};
