import { motion } from "framer-motion";
import { useApp } from "../state/AppContext";

/** Landing con estética "Ledger": cuaderno de evidencias. */
export default function Landing() {
  const { entrarApp, tema, alternarTema } = useApp();

  return (
    <div className="min-h-screen" style={{ background: "var(--bg)", color: "var(--ink)" }}>
      <nav
        className="mx-auto flex max-w-6xl items-center justify-between px-6 py-5"
        style={{ borderBottom: "1px solid var(--line)" }}
      >
        <span style={{ fontFamily: "var(--font-display)", fontSize: "1.35rem" }}>
          Evidentia<span className="accent-a">·</span>RAG
        </span>
        <div className="flex items-center gap-4 text-sm">
          <button onClick={alternarTema} aria-label="Alternar tema" className="theme-ink-soft">
            {tema === "dark" ? "☀ Día" : "☾ Noche"}
          </button>
          <button
            onClick={entrarApp}
            className="rounded-md px-4 py-1.5 font-semibold"
            style={{ background: "var(--accent-a)", color: "#0c1512" }}
          >
            Abrir panel
          </button>
        </div>
      </nav>

      <main className="mx-auto grid max-w-6xl gap-10 px-6 py-20 md:grid-cols-2">
        <div>
          <motion.h1
            initial={{ opacity: 0, y: 18 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.55 }}
            style={{ fontFamily: "var(--font-display)", fontSize: "3.2rem", lineHeight: 1.08 }}
          >
            Cada respuesta,
            <br />
            <span className="accent-b">rastreable hasta su fuente.</span>
          </motion.h1>
          <motion.p
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ delay: 0.25, duration: 0.5 }}
            className="mt-6 max-w-lg leading-relaxed"
            style={{ color: "var(--ink-soft)" }}
          >
            Un RAG agéntico con recuperación híbrida, reranking por LLM y auto-verificación.
            Un director de orquesta decide qué agente especializado —Recursos Humanos,
            Mantenimiento u Onboarding— consulta tus documentos, y cada afirmación lleva el sello
            de su chunk exacto.
          </motion.p>
          <div className="mt-8 flex gap-3">
            <button
              onClick={entrarApp}
              className="btn-accent-a rounded-md px-6 py-2.5 font-semibold"
              data-testid="cta-principal"
            >
              Empezar a consultar
            </button>
          </div>
        </div>

        <motion.ol
          initial={{ opacity: 0, x: 24 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ delay: 0.15, duration: 0.55 }}
          className="space-y-4 rounded-xl p-8"
          style={{ background: "var(--bg-elev)", border: "1px solid var(--line)" }}
        >
          {[
            ["Director de orquesta", "solo ve metadatos y reparte la pregunta entre agentes especializados."],
            ["Recuperación híbrida", "vectores + texto completo fusionados con Reciprocal Rank Fusion."],
            ["Citas verificables", "sellos que expanden al fragmento exacto, página incluida."],
            ["Auto-verificación", "una comprobación posterior valida cada respuesta contra sus fuentes."],
            ["Trazabilidad", "inspecciona etapa a etapa cómo se construyó cada respuesta."],
          ].map(([titulo, detalle], i) => (
            <li key={titulo} className="flex gap-4">
              <span
                className="shrink-0"
                style={{ fontFamily: "var(--font-mono)", color: "var(--accent-b)" }}
              >
                {String(i + 1).padStart(2, "0")}
              </span>
              <p className="text-sm leading-snug">
                <strong>{titulo}</strong> — <span className="theme-ink-soft">{detalle}</span>
              </p>
            </li>
          ))}
        </motion.ol>
      </main>

      <footer className="mx-auto max-w-6xl px-6 pb-10 text-xs theme-ink-soft">
        Prototipo funcional · .NET 10 · Python/LangChain · PostgreSQL + pgvector
      </footer>
    </div>
  );
}
