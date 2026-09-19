import { useCallback, useEffect, useMemo, useState } from "react";
import { motion, useReducedMotion } from "framer-motion";
import { Search, X } from "lucide-react";
import { useApp } from "../state/AppContext";
import { ETIQUETA_ROL, type DominioInfo, type UsuarioAdmin, type UsuarioPermitido } from "../lib/types";
import DialogoUsuario from "./DialogoUsuario";
import DialogoPermitido from "./DialogoPermitido";
import DialogoDominio from "./DialogoDominio";

type Pestana = "usuarios" | "permitidos" | "dominios";

/**
 * Panel de administración (solo superusuario) con dos pestañas: cuentas locales
 * (usuario/contraseña) y lista blanca de acceso Google. Tabla de filas tipo card
 * con buscador; el alta y la edición viven en diálogos.
 */
export default function AdminUsuarios({ onClose }: { onClose: () => void }) {
  const reduceMotion = useReducedMotion() ?? false;
  const { proveedoresDisponibles } = useApp();
  const conGoogle = proveedoresDisponibles.includes("google");
  const [pestana, setPestana] = useState<Pestana>("usuarios");

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
        aria-label="Administración de acceso"
        initial={reduceMotion ? { opacity: 1 } : { opacity: 0, y: 14, scale: 0.98 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: 0.25 }}
        className="flex max-h-[86vh] w-[640px] max-w-[94vw] flex-col rounded-2xl"
        style={{
          background: "var(--bg-elev)",
          border: "1px solid var(--line)",
          color: "var(--ink)",
          boxShadow: "var(--shadow-xl)",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-4 border-b px-6 py-4" style={{ borderColor: "var(--line)" }}>
          <div>
            <h2 className="text-lg font-semibold leading-tight">Administración de acceso</h2>
            <p className="text-xs" style={{ color: "var(--ink-soft)" }}>
              Cuentas locales y lista blanca de Google
            </p>
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

        <div className="flex gap-1 border-b px-6 pt-3" style={{ borderColor: "var(--line)" }} role="tablist">
          {(conGoogle ? (["usuarios", "permitidos", "dominios"] as Pestana[]) : (["usuarios", "dominios"] as Pestana[])).map((p) => (
              <button
                key={p}
                type="button"
                role="tab"
                aria-selected={pestana === p}
                onClick={() => setPestana(p)}
                className="rounded-t-md px-3 py-1.5 text-sm cursor-pointer"
                style={
                  pestana === p
                    ? { color: "var(--accent-a)", borderBottom: "2px solid var(--accent-a)" }
                    : { color: "var(--ink-soft)" }
                }
              >
                {p === "usuarios" ? "Usuarios locales" : p === "permitidos" ? "Lista blanca Google" : "Dominios"}
              </button>
            ))}
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-6 py-4">
          {pestana === "usuarios" && <TablaUsuarios />}
          {pestana === "permitidos" && <TablaPermitidos />}
          {pestana === "dominios" && <TablaDominios />}
        </div>
      </motion.div>
    </div>
  );
}

/** Buscador con icono, reutilizado en ambas pestañas. */
function Buscador({ valor, onCambio, etiqueta }: { valor: string; onCambio: (v: string) => void; etiqueta: string }) {
  return (
    <label className="relative mb-3 block">
      <Search size={14} className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2" style={{ color: "var(--ink-soft)" }} />
      <input
        value={valor}
        onChange={(e) => onCambio(e.target.value)}
        placeholder={etiqueta}
        aria-label={etiqueta}
        className="w-full rounded-md py-1.5 pl-8 pr-2 text-sm outline-none focus:ring-1"
        style={{ background: "var(--bg)", border: "1px solid var(--line)", color: "var(--ink)" }}
      />
    </label>
  );
}

function etiquetaRol(rol: UsuarioAdmin["rol"]) {
  return (
    <span
      className="rounded-full px-2 py-0.5 text-[11px] font-medium"
      style={{ background: "color-mix(in oklab, var(--accent-a) 12%, transparent)", color: "var(--accent-a)" }}
    >
      {ETIQUETA_ROL[rol]}
    </span>
  );
}

/** Pestaña de cuentas locales: tabla con buscador, alta y edición en diálogo. */
function TablaUsuarios() {
  const { listarUsuarios, actualizarUsuario, borrarUsuario, etiquetaDominio: etiquetaCtx, dominios: dominiosCtx } = useApp();
  const etiquetaDominio = etiquetaCtx ?? ((c: string) => c);
  const dominios = dominiosCtx ?? [];

  const [usuarios, setUsuarios] = useState<UsuarioAdmin[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busqueda, setBusqueda] = useState("");
  const [dialogo, setDialogo] = useState<{ abierto: boolean; usuario: UsuarioAdmin | null }>({ abierto: false, usuario: null });
  const [borrandoId, setBorrandoId] = useState<string | null>(null);

  const cargar = useCallback(async () => {
    try {
      setUsuarios(await listarUsuarios());
    } catch {
      setError("No se pudo cargar la lista de usuarios.");
    }
  }, [listarUsuarios]);

  useEffect(() => {
    void cargar();
  }, [cargar]);

  const filtrados = useMemo(() => {
    const q = busqueda.trim().toLowerCase();
    if (!q || !usuarios) return usuarios ?? [];
    return usuarios.filter((u) =>
      [u.usuario, u.nombre ?? "", u.email ?? "", ETIQUETA_ROL[u.rol], ...u.dominios.map((d) => etiquetaDominio(d))]
        .join(" ")
        .toLowerCase()
        .includes(q),
    );
  }, [usuarios, busqueda, etiquetaDominio]);

  const cambiarActivo = async (u: UsuarioAdmin, activo: boolean) => {
    try {
      const actualizado = await actualizarUsuario(u.id, { activo });
      setUsuarios((lista) => lista?.map((x) => (x.id === u.id ? actualizado : x)) ?? null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cambiar el estado del usuario.");
    }
  };

  const eliminar = async (u: UsuarioAdmin) => {
    setError(null);
    try {
      await borrarUsuario(u.id);
      setUsuarios((lista) => lista?.filter((x) => x.id !== u.id) ?? null);
      setBorrandoId(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo eliminar el usuario.");
    }
  };

  return (
    <div>
      <div className="mb-3 flex items-center justify-between gap-3">
        <div className="flex-1">
          <Buscador valor={busqueda} onCambio={setBusqueda} etiqueta="Buscar por usuario, nombre, email, rol o dominio…" />
        </div>
        <button
          type="button"
          onClick={() => setDialogo({ abierto: true, usuario: null })}
          className="btn-accent-a mb-3 shrink-0 rounded-md px-3 py-1.5 text-sm font-semibold cursor-pointer"
        >
          + Nuevo usuario
        </button>
      </div>

      {error && (
        <p className="mb-2 text-xs" role="alert" style={{ color: "var(--danger)" }}>
          {error}
        </p>
      )}

      <div className="space-y-1.5">
        {usuarios === null ? (
          <p className="py-6 text-center text-xs" style={{ color: "var(--ink-soft)" }}>
            Cargando usuarios…
          </p>
        ) : filtrados.length === 0 ? (
          <p className="py-6 text-center text-xs" style={{ color: "var(--ink-soft)" }}>
            {busqueda ? "Sin resultados para esta búsqueda." : "Todavía no hay cuentas locales. Crea la primera arriba."}
          </p>
        ) : (
          filtrados.map((u) => (
            <div key={u.id}>
              <div
                className="flex items-center gap-3 rounded-lg px-3 py-2"
                style={{ background: "var(--bg)", border: "1px solid var(--line)" }}
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">{u.usuario}</p>
                  <p className="truncate text-[11px]" style={{ color: "var(--ink-soft)" }}>
                    {u.nombre || u.email || "—"}
                  </p>
                  {u.rol === "teamleader" && (
                    <p className="mt-0.5 truncate text-[11px]" style={{ color: "var(--accent-a)" }}>
                      {(u.dominios.length > 0 ? u.dominios : dominios.map((d) => d.clave)).map((d) => etiquetaDominio(d)).join(" · ")}
                    </p>
                  )}
                </div>
                {etiquetaRol(u.rol)}
                <label className="flex items-center gap-1.5 text-xs" style={{ color: "var(--ink-soft)" }}>
                  <input
                    type="checkbox"
                    checked={u.activo}
                    onChange={(e) => void cambiarActivo(u, e.target.checked)}
                    aria-label={`Activo ${u.usuario}`}
                    className="accent-[var(--accent-a)]"
                  />
                  Activo
                </label>
                <button
                  type="button"
                  onClick={() => setDialogo({ abierto: true, usuario: u })}
                  aria-label={`Editar ${u.usuario}`}
                  className="rounded-md border px-2 py-1 text-xs cursor-pointer"
                  style={{ borderColor: "var(--line)", color: "var(--accent-a)" }}
                >
                  Editar
                </button>
                <button
                  type="button"
                  onClick={() => setBorrandoId(u.id)}
                  aria-label={`Eliminar ${u.usuario}`}
                  className="rounded-md border px-2 py-1 text-xs cursor-pointer"
                  style={{ borderColor: "var(--line)", color: "var(--danger)" }}
                >
                  ✕
                </button>
              </div>
              {borrandoId === u.id && (
                <div
                  className="mt-1 flex items-center gap-2 rounded-lg px-3 py-2 text-xs"
                  style={{ border: "1px solid var(--danger)" }}
                >
                  <span className="flex-1">¿Eliminar a {u.usuario}?</span>
                  <button
                    type="button"
                    onClick={() => void eliminar(u)}
                    className="rounded px-2 py-1 font-semibold text-white"
                    style={{ background: "var(--danger)" }}
                  >
                    Eliminar
                  </button>
                  <button type="button" onClick={() => setBorrandoId(null)} className="rounded px-2 py-1" style={{ color: "var(--ink-soft)" }}>
                    Cancelar
                  </button>
                </div>
              )}
            </div>
          ))
        )}
      </div>

      {dialogo.abierto && (
        <DialogoUsuario
          usuario={dialogo.usuario}
          onGuardado={(guardado) => {
            setUsuarios((lista) =>
              dialogo.usuario
                ? (lista?.map((x) => (x.id === guardado.id ? guardado : x)) ?? null)
                : [...(lista ?? []), guardado],
            );
          }}
          onClose={() => setDialogo({ abierto: false, usuario: null })}
          onError={setError}
        />
      )}
    </div>
  );
}

/** Pestaña de lista blanca Google: tabla con buscador y alta en diálogo. */
function TablaPermitidos() {
  const { listarPermitidos, actualizarPermitido, borrarPermitido } = useApp();

  const [entradas, setEntradas] = useState<UsuarioPermitido[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busqueda, setBusqueda] = useState("");
  const [dialogoAbierto, setDialogoAbierto] = useState(false);
  const [borrandoId, setBorrandoId] = useState<string | null>(null);

  const cargar = useCallback(async () => {
    try {
      setEntradas(await listarPermitidos());
    } catch {
      setError("No se pudo cargar la lista blanca.");
    }
  }, [listarPermitidos]);

  useEffect(() => {
    void cargar();
  }, [cargar]);

  const filtradas = useMemo(() => {
    const q = busqueda.trim().toLowerCase();
    if (!q || !entradas) return entradas ?? [];
    return entradas.filter((e) => `${e.email ?? ""} ${e.dominio ?? ""}`.toLowerCase().includes(q));
  }, [entradas, busqueda]);

  const cambiarActivo = async (e: UsuarioPermitido, activo: boolean) => {
    try {
      const actualizada = await actualizarPermitido(e.id, { activo });
      setEntradas((lista) => lista?.map((x) => (x.id === e.id ? actualizada : x)) ?? null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo cambiar el estado de la entrada.");
    }
  };

  const eliminar = async (e: UsuarioPermitido) => {
    setError(null);
    try {
      await borrarPermitido(e.id);
      setEntradas((lista) => lista?.filter((x) => x.id !== e.id) ?? null);
      setBorrandoId(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo eliminar la entrada.");
    }
  };

  return (
    <div>
      <div className="mb-3 flex items-center justify-between gap-3">
        <div className="flex-1">
          <Buscador valor={busqueda} onCambio={setBusqueda} etiqueta="Buscar por email o dominio…" />
        </div>
        <button
          type="button"
          onClick={() => setDialogoAbierto(true)}
          className="btn-accent-a mb-3 shrink-0 rounded-md px-3 py-1.5 text-sm font-semibold cursor-pointer"
        >
          + Nueva entrada
        </button>
      </div>

      {error && (
        <p className="mb-2 text-xs" role="alert" style={{ color: "var(--danger)" }}>
          {error}
        </p>
      )}

      <div className="space-y-1.5">
        {entradas === null ? (
          <p className="py-6 text-center text-xs" style={{ color: "var(--ink-soft)" }}>
            Cargando lista blanca…
          </p>
        ) : filtradas.length === 0 ? (
          <p className="py-6 text-center text-xs" style={{ color: "var(--ink-soft)" }}>
            {busqueda ? "Sin resultados para esta búsqueda." : "La lista está vacía: nadie puede entrar con Google."}
          </p>
        ) : (
          filtradas.map((e) => (
            <div key={e.id}>
              <div
                className="flex items-center gap-3 rounded-lg px-3 py-2"
                style={{ background: "var(--bg)", border: "1px solid var(--line)" }}
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">{e.email ?? e.dominio}</p>
                  <p className="text-[11px]" style={{ color: "var(--ink-soft)" }}>
                    {e.email ? "Email exacto" : "Dominio completo"}
                  </p>
                </div>
                <label className="flex items-center gap-1.5 text-xs" style={{ color: "var(--ink-soft)" }}>
                  <input
                    type="checkbox"
                    checked={e.activo}
                    onChange={(ev) => void cambiarActivo(e, ev.target.checked)}
                    aria-label={`Activa ${e.email ?? e.dominio}`}
                    className="accent-[var(--accent-a)]"
                  />
                  Activa
                </label>
                <button
                  type="button"
                  onClick={() => setBorrandoId(e.id)}
                  aria-label={`Eliminar ${e.email ?? e.dominio}`}
                  className="rounded-md border px-2 py-1 text-xs cursor-pointer"
                  style={{ borderColor: "var(--line)", color: "var(--danger)" }}
                >
                  ✕
                </button>
              </div>
              {borrandoId === e.id && (
                <div
                  className="mt-1 flex items-center gap-2 rounded-lg px-3 py-2 text-xs"
                  style={{ border: "1px solid var(--danger)" }}
                >
                  <span className="flex-1">¿Quitar {e.email ?? e.dominio} de la lista?</span>
                  <button
                    type="button"
                    onClick={() => void eliminar(e)}
                    className="rounded px-2 py-1 font-semibold text-white"
                    style={{ background: "var(--danger)" }}
                  >
                    Quitar
                  </button>
                  <button type="button" onClick={() => setBorrandoId(null)} className="rounded px-2 py-1" style={{ color: "var(--ink-soft)" }}>
                    Cancelar
                  </button>
                </div>
              )}
            </div>
          ))
        )}
      </div>

      {dialogoAbierto && (
        <DialogoPermitido
          onGuardado={(entrada) => setEntradas((lista) => [...(lista ?? []), entrada])}
          onClose={() => setDialogoAbierto(false)}
          onError={setError}
        />
      )}
    </div>
  );
}

function TablaDominios() {
  const { dominios: dominiosCtx, refrescarDominios, borrarDominio } = useApp();
  const dominios = dominiosCtx ?? [];
  const [error, setError] = useState<string | null>(null);
  const [busqueda, setBusqueda] = useState("");
  const [dialogo, setDialogo] = useState<{ abierto: boolean; dominio: DominioInfo | null }>({ abierto: false, dominio: null });
  const [borrandoClave, setBorrandoClave] = useState<string | null>(null);

  useEffect(() => {
    void Promise.resolve(refrescarDominios()).catch(() => setError("No se pudo cargar los dominios."));
  }, [refrescarDominios]);

  const filtrados = useMemo(() => {
    const q = busqueda.trim().toLowerCase();
    if (!q) return dominios;
    return dominios.filter((d) => `${d.clave} ${d.etiqueta} ${d.descripcion ?? ""}`.toLowerCase().includes(q));
  }, [dominios, busqueda]);

  const eliminar = async (d: DominioInfo) => {
    setError(null);
    try {
      await borrarDominio(d.clave);
      setBorrandoClave(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudo eliminar el dominio.");
    }
  };

  return (
    <div>
      <div className="mb-3 flex items-center justify-between gap-3">
        <div className="flex-1">
          <Buscador valor={busqueda} onCambio={setBusqueda} etiqueta="Buscar por clave, etiqueta o descripción…" />
        </div>
        <button
          type="button"
          onClick={() => setDialogo({ abierto: true, dominio: null })}
          className="btn-accent-a mb-3 shrink-0 rounded-md px-3 py-1.5 text-sm font-semibold cursor-pointer"
        >
          + Nuevo dominio
        </button>
      </div>
      {error && (
        <p className="mb-2 text-xs" role="alert" style={{ color: "var(--danger)" }}>
          {error}
        </p>
      )}
      <div className="space-y-1.5">
        {filtrados.length === 0 ? (
          <p className="py-6 text-center text-xs" style={{ color: "var(--ink-soft)" }}>
            {busqueda ? "Sin resultados para esta búsqueda." : "Todavía no hay dominios."}
          </p>
        ) : (
          filtrados.map((d) => (
            <div key={d.clave}>
              <div className="flex items-center gap-3 rounded-lg px-3 py-2" style={{ background: "var(--bg)", border: "1px solid var(--line)" }}>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">{d.etiqueta}</p>
                  <p className="truncate text-[11px]" style={{ color: "var(--ink-soft)" }}>
                    {d.clave}{d.descripcion ? ` · ${d.descripcion}` : ""}
                  </p>
                </div>
                <button
                  type="button"
                  onClick={() => setDialogo({ abierto: true, dominio: d })}
                  aria-label={`Editar ${d.clave}`}
                  className="rounded-md border px-2 py-1 text-xs cursor-pointer"
                  style={{ borderColor: "var(--line)", color: "var(--accent-a)" }}
                >
                  Editar
                </button>
                <button
                  type="button"
                  onClick={() => setBorrandoClave(d.clave)}
                  aria-label={`Eliminar ${d.clave}`}
                  className="rounded-md border px-2 py-1 text-xs cursor-pointer"
                  style={{ borderColor: "var(--line)", color: "var(--danger)" }}
                >
                  ✕
                </button>
              </div>
              {borrandoClave === d.clave && (
                <div className="mt-1 flex items-center gap-2 rounded-lg px-3 py-2 text-xs" style={{ border: "1px solid var(--danger)" }}>
                  <span className="flex-1">¿Eliminar {d.clave}? Se bloquea si tiene documentos, carpetas o usuarios.</span>
                  <button type="button" onClick={() => void eliminar(d)} className="rounded px-2 py-1 font-semibold text-white" style={{ background: "var(--danger)" }}>
                    Eliminar
                  </button>
                  <button type="button" onClick={() => setBorrandoClave(null)} className="rounded px-2 py-1" style={{ color: "var(--ink-soft)" }}>
                    Cancelar
                  </button>
                </div>
              )}
            </div>
          ))
        )}
      </div>
      {dialogo.abierto && (
        <DialogoDominio
          dominio={dialogo.dominio}
          onGuardado={() => void refrescarDominios()}
          onClose={() => setDialogo({ abierto: false, dominio: null })}
          onError={setError}
        />
      )}
    </div>
  );
}
