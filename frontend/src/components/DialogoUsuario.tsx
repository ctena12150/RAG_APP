import { useState } from "react";
import type { FormEvent } from "react";
import { useApp } from "../state/AppContext";
import { ETIQUETA_ROL, ROLES, type Dominio, type Rol, type UsuarioAdmin } from "../lib/types";
import Dialogo, { campoDialogo, estiloCampoDialogo } from "./Dialogo";

/**
 * Alta y edición de un usuario local. En edición el nombre de usuario es de solo
 * lectura; la contraseña vacía conserva la actual. Los dominios solo aplican al
 * teamleader (sin marcar ninguno = todos).
 */
export default function DialogoUsuario({
  usuario,
  onGuardado,
  onClose,
  onError,
}: {
  usuario: UsuarioAdmin | null;
  onGuardado: (u: UsuarioAdmin) => void;
  onClose: () => void;
  onError: (msg: string | null) => void;
}) {
  const { crearUsuario, actualizarUsuario, dominios: catalogoCtx, etiquetaDominio: etiquetaCtx } = useApp();
  const catalogo = catalogoCtx ?? [];
  const etiquetaDominio = etiquetaCtx ?? ((c: string) => c);
  const editando = usuario !== null;

  const [identificador, setIdentificador] = useState(usuario?.usuario ?? "");
  const [nombre, setNombre] = useState(usuario?.nombre ?? "");
  const [email, setEmail] = useState(usuario?.email ?? "");
  const [rol, setRol] = useState<Rol>(usuario?.rol ?? "usuario");
  const [dominios, setDominios] = useState<Dominio[]>([...(usuario?.dominios ?? catalogo.map((d) => d.clave))]);
  const [contrasena, setContrasena] = useState("");
  const [guardando, setGuardando] = useState(false);

  const alternarDominio = (d: Dominio) =>
    setDominios((lista) => (lista.includes(d) ? lista.filter((x) => x !== d) : [...lista, d]));

  const guardar = async (e: FormEvent) => {
    e.preventDefault();
    onError(null);
    setGuardando(true);
    try {
      const guardado = editando
        ? await actualizarUsuario(usuario.id, {
            rol,
            nombre: nombre || null,
            email: email || null,
            contrasena: contrasena || undefined,
            dominios: rol === "teamleader" ? dominios : undefined,
          })
        : await crearUsuario({
            usuario: identificador,
            contrasena,
            rol,
            email: email || null,
            nombre: nombre || null,
            dominios: rol === "teamleader" ? dominios : undefined,
          });
      onGuardado(guardado);
      onClose();
    } catch (err) {
      onError(err instanceof Error ? err.message : "No se pudo guardar el usuario.");
    } finally {
      setGuardando(false);
    }
  };

  return (
    <Dialogo
      titulo={editando ? `Editar ${usuario.usuario}` : "Nuevo usuario"}
      subtitulo={editando ? "Los cambios de rol y dominios aplican al siguiente login" : "Cuenta de acceso usuario/contraseña"}
      onClose={onClose}
    >
      <form onSubmit={(e) => void guardar(e)} className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <label className="flex flex-col gap-1 text-xs" style={{ color: "var(--ink-soft)" }}>
            Usuario
            <input
              value={identificador}
              onChange={(e) => setIdentificador(e.target.value)}
              required
              disabled={editando}
              className={`${campoDialogo} disabled:opacity-50`}
              style={estiloCampoDialogo}
            />
          </label>
          <label className="flex flex-col gap-1 text-xs" style={{ color: "var(--ink-soft)" }}>
            Nombre
            <input value={nombre} onChange={(e) => setNombre(e.target.value)} className={campoDialogo} style={estiloCampoDialogo} />
          </label>
          <label className="flex flex-col gap-1 text-xs" style={{ color: "var(--ink-soft)" }}>
            Email
            <input value={email} type="email" onChange={(e) => setEmail(e.target.value)} className={campoDialogo} style={estiloCampoDialogo} />
          </label>
          <label className="flex flex-col gap-1 text-xs" style={{ color: "var(--ink-soft)" }}>
            Rol
            <select
              value={rol}
              onChange={(e) => {
                const nuevo = e.target.value as Rol;
                setRol(nuevo);
                if (nuevo === "teamleader" && dominios.length === 0) setDominios(catalogo.map((d) => d.clave));
              }}
              className={campoDialogo}
              style={estiloCampoDialogo}
            >
              {ROLES.map((r) => (
                <option key={r} value={r}>
                  {ETIQUETA_ROL[r]}
                </option>
              ))}
            </select>
          </label>
        </div>
        {rol === "teamleader" && (
          <fieldset className="flex flex-col gap-1.5 text-xs" style={{ color: "var(--ink-soft)" }}>
            <legend>Dominios gestionables (sin marcar ninguno = todos)</legend>
            <div className="flex flex-wrap gap-3">
              {catalogo.map((d) => (
                <label key={d.clave} className="flex items-center gap-1.5" style={{ color: "var(--ink)" }}>
                  <input
                    type="checkbox"
                    checked={dominios.includes(d.clave)}
                    onChange={() => alternarDominio(d.clave)}
                    className="accent-[var(--accent-a)]"
                  />
                  {etiquetaDominio(d.clave)}
                </label>
              ))}
            </div>
          </fieldset>
        )}
        <label className="flex flex-col gap-1 text-xs" style={{ color: "var(--ink-soft)" }}>
          {editando ? "Nueva contraseña (vacío = sin cambio)" : "Contraseña (mín. 8 caracteres)"}
          <input
            value={contrasena}
            type="password"
            minLength={8}
            required={!editando}
            onChange={(e) => setContrasena(e.target.value)}
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
            {guardando ? "Guardando…" : editando ? "Guardar" : "Crear usuario"}
          </button>
        </div>
      </form>
    </Dialogo>
  );
}
