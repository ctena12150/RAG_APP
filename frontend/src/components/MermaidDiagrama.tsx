import { useEffect, useState } from "react";

/** Diagrama Mermaid renderizado en cliente; fallback a bloque de código si falla. */
export default function MermaidDiagrama({ codigo }: { codigo: string }) {
  const [svg, setSvg] = useState<string | null>(null);
  const [fallo, setFallo] = useState(false);

  useEffect(() => {
    let cancelado = false;
    import("mermaid")
      .then(async (mermaid) => {
        mermaid.default.initialize({ startOnLoad: false, theme: "dark", securityLevel: "strict" });
        const { svg } = await mermaid.default.render(`mmd-${Math.random().toString(36).slice(2)}`, codigo.trim());
        if (!cancelado) setSvg(svg);
      })
      .catch(() => !cancelado && setFallo(true));
    return () => {
      cancelado = true;
    };
  }, [codigo]);

  if (fallo)
    return (
      <pre className="my-2 overflow-x-auto rounded-md p-3 text-xs" style={{ border: "1px solid var(--line)" }}>
        <code>{codigo}</code>
      </pre>
    );

  if (!svg) return <p className="text-xs theme-ink-soft">renderizando diagrama…</p>;

  return (
    <div
      className="my-2 overflow-x-auto"
      // el SVG lo genera mermaid localmente a partir del código del documento
      dangerouslySetInnerHTML={{ __html: svg }}
    />
  );
}
