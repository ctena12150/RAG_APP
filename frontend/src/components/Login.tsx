import { useState } from "react";
import type { FormEvent } from "react";
import { useApp } from "../state/AppContext";

/** Icono simple de marca Google (evita dependencias: lucide no trae logos de proveedores). */
function GoogleMark() {
  return (
    <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
      <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 0 1-2.2 3.32v2.77h3.57c2.08-1.92 3.27-4.74 3.27-8.1Z" />
      <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84A11 11 0 0 0 12 23Z" />
      <path fill="#FBBC05" d="M5.84 14.1a6.6 6.6 0 0 1 0-4.2V7.06H2.18a11 11 0 0 0 0 9.88l3.66-2.84Z" />
      <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15A11 11 0 0 0 2.18 7.06l3.66 2.84c.87-2.6 3.3-4.52 6.16-4.52Z" />
    </svg>
  );
}

/**
 * Opciones de inicio de sesión: botón de Google (OIDC + lista blanca) y/o formulario local
 * de usuario/contraseña (app.usuarios). Solo muestra los proveedores expuestos por el backend.
 */
export default function LoginOptions({ motivo, className }: { motivo?: string; className?: string }) {
  const { proveedoresDisponibles, iniciarSesion, iniciarSesionLocal } = useApp();
  const google = proveedoresDisponibles.includes("google");
  const local = proveedoresDisponibles.includes("local");
  const [usuario, setUsuario] = useState("");
  const [contrasena, setContrasena] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [errorLocal, setErrorLocal] = useState<string | null>(null);

  if (!google && !local) return null;

  const boton =
    "flex w-full items-center justify-center gap-3 rounded-full border px-6 py-3 font-medium transition hover:brightness-110 cursor-pointer";
  const campo =
    "w-full rounded-full border bg-transparent px-4 py-2.5 text-sm outline-none transition focus:brightness-110";

  const enviarLocal = async (e: FormEvent) => {
    e.preventDefault();
    setErrorLocal(null);
    if (!usuario.trim() || !contrasena) {
      setErrorLocal("Introduce tu usuario y contraseña.");
      return;
    }
    setEnviando(true);
    try {
      await iniciarSesionLocal(usuario.trim(), contrasena);
    } catch (err) {
      setErrorLocal(err instanceof Error ? err.message : "No se pudo iniciar sesión.");
      setEnviando(false);
    }
  };

  return (
    <div className={`flex w-full max-w-xs flex-col items-center gap-4 ${className ?? ""}`}>
      {motivo && (
        <p className="text-sm leading-relaxed" style={{ color: "var(--ink-soft)" }}>
          {motivo}
        </p>
      )}
      <div className="flex w-full flex-col gap-2">
        {google && (
          <button
            type="button"
            onClick={() => iniciarSesion("google")}
            className={boton}
            style={{ background: "var(--bg-elev)", borderColor: "var(--line)", color: "var(--ink)" }}
          >
            <GoogleMark />
            Continuar con Google
          </button>
        )}
        {google && local && (
          <span className="flex items-center gap-3 py-1 text-xs uppercase tracking-widest" style={{ color: "var(--ink-soft)" }}>
            <span style={{ flex: 1, height: 1, background: "var(--line)" }} />
            o
            <span style={{ flex: 1, height: 1, background: "var(--line)" }} />
          </span>
        )}
        {local && (
          <form onSubmit={enviarLocal} className="flex w-full flex-col gap-2">
            <input
              type="text"
              value={usuario}
              onChange={(e) => setUsuario(e.target.value)}
              placeholder="Usuario"
              autoComplete="username"
              aria-label="Usuario"
              className={campo}
              style={{ borderColor: "var(--line)", color: "var(--ink)" }}
            />
            <input
              type="password"
              value={contrasena}
              onChange={(e) => setContrasena(e.target.value)}
              placeholder="Contraseña"
              autoComplete="current-password"
              aria-label="Contraseña"
              className={campo}
              style={{ borderColor: "var(--line)", color: "var(--ink)" }}
            />
            {errorLocal && (
              <p className="text-xs" style={{ color: "var(--danger)", textAlign: "center" }} role="alert">
                {errorLocal}
              </p>
            )}
            <button
              type="submit"
              disabled={enviando}
              className={boton}
              style={{
                background: "linear-gradient(135deg, var(--accent-a), var(--accent-b))",
                borderColor: "transparent",
                color: "var(--accent-ink, #0c1512)",
              }}
            >
              {enviando ? "Entrando…" : "Entrar"}
            </button>
          </form>
        )}
      </div>
    </div>
  );
}