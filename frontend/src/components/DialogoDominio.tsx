import { useState } from "react";
import type { FormEvent } from "react";
import { useApp } from "../state/AppContext";
import type { DominioInfo } from "../lib/types";
import Dialogo, { campoDialogo, estiloCampoDialogo } from "./Dialogo";

export default function DialogoDominio({
  dominio,
  onGuardado,
  onClose,
  onError,
}: {
  dominio: DominioInfo | null;
  onGuardado: (d: DominioInfo) => void;
  onClose: () => void;
  onError: (msg: string | null) => void;
}) {
  const { crearDominio, actualizarDominio } = useApp();
  const editando = dominio !== null;
  const [clave, setClave] = useState(dominio?.clave ?? "");
  const [etiqueta, setEtiqueta] = useState(dominio?.etiqueta ?? "");
  const [descripcion, setDescripcion] = useState(dominio?.descripcion ?? "");
  const [guardando, setGuardando] = useState(false);

  const guardar = async (e: FormEvent) => {
    e.preventDefault();
    onError(null);
    setGuardando(true);
    try {
      const guardado = editando
        ? await actualizarDominio(dominio.clave, { etiqueta, descripcion })
        : await crearDominio({ clave, etiqueta, descripcion });
      onGuardado(guardado);
      onClose();
    } catch (err) {
      onError(err instanceof Error ? err.message : "No se pudo guardar el dominio.");
    } finally {
      setGuardando(false);
    }
  };

  return (
    <Dialogo
      titulo={editando ? `Editar ${dominio.clave}` : "Nuevo dominio"}
      subtitulo={editando ? "La clave es inmutable" : "Clave en minúsculas, 2-30 caracteres"}
      onClose={onClose}
    >
      <form onSubmit={(e) => void guardar(e)} className="space-y-3">
        <label className="flex flex-col gap-1 text-xs" style={{ color: "var(--ink-soft)" }}>
          Clave
          <input
            value={clave}
            onChange={(e) => setClave(e.target.value.toLowerCase())}
            required
            disabled={editando}
            pattern="[a-z0-9-]{2,30}"
            className={`${campoDialogo} disabled:opacity-50`}
            style={estiloCampoDialogo}
          />
        </label>
        <label className="flex flex-col gap-1 text-xs" style={{ color: "var(--ink-soft)" }}>
          Etiqueta
          <input
            value={etiqueta}
            onChange={(e) => setEtiqueta(e.target.value)}
            required
            maxLength={100}
            className={campoDialogo}
            style={estiloCampoDialogo}
          />
        </label>
        <label className="flex flex-col gap-1 text-xs" style={{ color: "var(--ink-soft)" }}>
          Descripción (ámbito para el Director)
          <input
            value={descripcion}
            onChange={(e) => setDescripcion(e.target.value)}
            maxLength={300}
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
            {guardando ? "Guardando…" : editando ? "Guardar" : "Crear dominio"}
          </button>
        </div>
      </form>
    </Dialogo>
  );
}
