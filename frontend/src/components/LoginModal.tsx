import { useEffect } from "react";
import { motion, useReducedMotion } from "framer-motion";
import { X } from "lucide-react";
import { LogoMark } from "./LogoMark";
import LoginOptions from "./Login";

/**
 * Modal de inicio de sesión: overlay + tarjeta centrada con las vías expuestas por
 * el backend (Google y/o usuario-contraseña). Cierra con Escape, click fuera o la X.
 */
export default function LoginModal({ onClose }: { onClose: () => void }) {
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
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ background: "var(--overlay)" }}
      onClick={onClose}
    >
      <motion.div
        role="dialog"
        aria-modal="true"
        aria-label="Iniciar sesión"
        initial={reduceMotion ? { opacity: 1 } : { opacity: 0, y: 14, scale: 0.98 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: 0.25 }}
        className="w-[400px] max-w-[92vw] rounded-2xl p-6"
        style={{
          background: "var(--bg-elev)",
          border: "1px solid var(--line)",
          color: "var(--ink)",
          boxShadow: "var(--shadow-xl)",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-5 flex items-start justify-between gap-4">
          <div className="flex items-center gap-3">
            <span
              className="flex h-10 w-10 items-center justify-center rounded-full"
              style={{ background: "linear-gradient(135deg, var(--accent-a), var(--accent-b))" }}
            >
              <LogoMark size={20} />
            </span>
            <div className="text-left">
              <h2 className="text-lg font-semibold leading-tight">Entrar al chat</h2>
              <p className="text-xs" style={{ color: "var(--ink-soft)" }}>
                Elige cómo identificarte
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Cerrar"
            className="flex h-8 w-8 items-center justify-center rounded-full border cursor-pointer transition hover:brightness-110"
            style={{ borderColor: "var(--line)", color: "var(--ink-soft)" }}
          >
            <X size={15} />
          </button>
        </div>

        <LoginOptions motivo="Tu sesión queda guardada en este navegador." className="max-w-none" />

        <p className="mt-5 text-center text-xs" style={{ color: "var(--ink-soft)" }}>
          Acceso restringido a usuarios autorizados.
        </p>
      </motion.div>
    </div>
  );
}
