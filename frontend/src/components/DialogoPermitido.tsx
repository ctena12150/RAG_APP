import { useState } from "react";
import type { FormEvent } from "react";
import { useApp } from "../state/AppContext";
import type { UsuarioPermitido } from "../lib/types";
import Dialogo, { campoDialogo, estiloCampoDialogo } from "./Dialogo";

type TipoEntrada = "email" | "dominio";

/**
 * Alta de una entrada de la lista blanca de acceso Google: un email exacto o un
 * dominio (la parte tras la @). El valor es inmutable tras el alta.
 */
export default function DialogoPermitido({
  onGuardado,
  onClose,
  onError,
}: {
  onGuardado: (e: UsuarioPermitido) => void;
  onClose: () => void;
  onError: (msg: string | null) => void;
}) {
  const { crearPermitido } = useApp();
  const [tipo, setTipo] = useState<TipoEntrada>("email");
  const [valor, setValor] = useState("");
  const [guardando, setGuardando] = useState(false);

  const guardar = async (e: FormEvent) => {
    e.preventDefault();
    onError(null);
    setGuardando(true);
    try {
      const entrada = await crearPermitido(
        tipo === "email" ? { email: valor, dominio: null } : { email: null, dominio: valor },
      );
      onGuardado(entrada);
      onClose();
    } catch (err) {
      onError(err instanceof Error ? err.message : "No se pudo añadir la entrada.");
    } finally {
      setGuardando(false);
    }
  };

  return (
    <Dialogo
      titulo="Nueva entrada permitida"
      subtitulo="Solo estas cuentas de Google podrán entrar al chat"
      onClose={onClose}
    >
      <form onSubmit={(e) => void guardar(e)} className="space-y-3">
        <div className="flex gap-4 text-sm" role="radiogroup" aria-label="Tipo de entrada">
          {(["email", "dominio"] as TipoEntrada[]).map((t) => (
            <label key={t} className="flex cursor-pointer items-center gap-1.5" style={{ color: "var(--ink)" }}>
              <input
                type="radio"
                name="tipo-entrada"
                checked={tipo === t}
                onChange={() => setTipo(t)}
                className="accent-[var(--accent-a)]"
              />
              {t === "email" ? "Email exacto" : "Dominio completo"}
            </label>
          ))}
        </div>
        <label className="flex flex-col gap-1 text-xs" style={{ color: "var(--ink-soft)" }}>
          {tipo === "email" ? "Email (p. ej. carla@empresa.com)" : "Dominio (p. ej. empresa.com)"}
          <input
            value={valor}
            onChange={(e) => setValor(e.target.value)}
            required
            placeholder={tipo === "email" ? "carla@empresa.com" : "empresa.com"}
            className={campoDialogo}
            style={estiloCampoDialogo}
          />
        </label>
        <div className="flex justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border px-3 py-1.5 text-sm cursor-pointer"
            style={{ borderColor: "var(--line)", color: "var(--ink-soft)" }}
          >
            Cancelar
          </button>
          <button
            type="submit"
            disabled={guardando}
            className="btn-accent-a rounded-md px-3 py-1.5 text-sm font-semibold disabled:opacity-40 cursor-pointer"
          >
            {guardando ? "Añadiendo…" : "Añadir"}
          </button>
        </div>
      </form>
    </Dialogo>
  );
}
