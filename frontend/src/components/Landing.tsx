import { lazy, Suspense } from "react";
import { motion, useReducedMotion } from "framer-motion";
import { ArrowRight, Moon, Sun } from "lucide-react";
import { useApp } from "../state/AppContext";
import { LogoMark } from "./LogoMark";
import { AmbientBackground } from "./AmbientBackground";
import { GrainOverlay } from "./GrainOverlay";
const SignalBackground = lazy(() => import("./hero/SignalBackground"));

export default function Landing() {
  const { entrarApp, tema, alternarTema } = useApp();
  const reduceMotion = useReducedMotion() ?? false;

  return (
    <div className="relative flex min-h-svh flex-col overflow-hidden" style={{ background: "var(--bg)", color: "var(--ink)" }}>
      <AmbientBackground />
      <Suspense fallback={null}>
        <SignalBackground reduceMotion={reduceMotion} />
      </Suspense>
      <GrainOverlay />

      <header className="relative z-10 flex items-center justify-between px-6 py-6 sm:px-10">
        <span className="flex items-center gap-2 font-mono text-xs uppercase tracking-[0.2em] text-ink-muted">
          <LogoMark size={18} />
          Evidentia<span className="accent-a">·</span>RAG
        </span>
        <button
          onClick={alternarTema}
          aria-label={tema === "dark" ? "Cambiar a modo claro" : "Cambiar a modo oscuro"}
          className="flex h-9 w-9 items-center justify-center rounded-full border cursor-pointer"
          style={{ borderColor: "var(--line)", color: "var(--ink-soft)", background: "var(--bg-elev)" }}
        >
          {tema === "dark" ? <Sun size={16} /> : <Moon size={16} />}
        </button>
      </header>

      <main className="relative z-10 flex flex-1 flex-col items-center justify-center px-6 text-center">
        <motion.div
          initial={{ opacity: 0, y: 18 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.55 }}
          className="flex max-w-2xl flex-col items-center gap-6"
        >
          <motion.span
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ delay: 0.15, duration: 0.5 }}
            className="font-mono text-xs uppercase tracking-[0.2em]"
            style={{ color: "var(--accent-a)" }}
          >
            Agentic · Grounded · Cited
          </motion.span>

          <motion.h1
            initial={{ opacity: 0, y: 18 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.2, duration: 0.55 }}
            style={{ fontFamily: "var(--font-display)", fontSize: "3.2rem", lineHeight: 1.08 }}
            className="tracking-tight"
          >
            Cada respuesta,
            <br />
            <span className="accent-b">rastreable hasta su fuente.</span>
          </motion.h1>

          <motion.p
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ delay: 0.4, duration: 0.5 }}
            className="max-w-md leading-relaxed"
            style={{ color: "var(--ink-soft)" }}
          >
            Un RAG agéntico con recuperación híbrida, reranking por LLM y auto-verificación.
            Un director de orquesta decide qué agente especializado —Recursos Humanos,
            Mantenimiento u Onboarding— consulta tus documentos, y cada afirmación lleva el sello
            de su chunk exacto.
          </motion.p>

          <motion.div
            initial={{ opacity: 0, y: 18 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.55, duration: 0.55 }}
          >
            <motion.button
              type="button"
              onClick={entrarApp}
              whileHover={reduceMotion ? {} : { scale: 1.03, y: -1 }}
              whileTap={reduceMotion ? {} : { scale: 0.97, y: 0 }}
              className="group mt-2 flex items-center gap-2 rounded-full px-6 py-3 font-medium"
              style={{
                background: "linear-gradient(135deg, var(--accent-a), var(--accent-b))",
                color: "#0c1512",
                boxShadow: "0 1px 0 0 rgba(255,255,255,0.15) inset, 0 8px 24px -8px var(--accent-a)",
              }}
            >
              Empezar a consultar
              <ArrowRight size={18} className="transition-transform duration-300 group-hover:translate-x-1" />
            </motion.button>
          </motion.div>
        </motion.div>
      </main>

      <footer className="relative z-10 mx-auto max-w-6xl px-6 pb-10 text-center text-xs" style={{ color: "var(--ink-soft)" }}>
        Prototipo funcional · .NET 10 · Python/LangChain · PostgreSQL + pgvector
      </footer>
    </div>
  );
}