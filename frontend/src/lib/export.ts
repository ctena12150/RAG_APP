import type { Conversacion, MensajeChat } from "./types";

/** Exporta una conversación a un Markdown portátil con citas y notas de verificación. */
export function exportarConversacionMarkdown(conversacion: Conversacion, mensajes: MensajeChat[]): string {
  const lineas: string[] = [
    `# ${conversacion.titulo}`,
    "",
    `_Exportada el ${new Date().toLocaleString("es-ES")}_`,
    "",
  ];

  for (const m of mensajes) {
    if (m.rol === "user") {
      lineas.push(`## Pregunta`, "", m.contenido, "");
    } else {
      lineas.push(`## Respuesta`, "", m.contenido, "");
      if (m.fuentes?.length) {
        lineas.push(`### Fuentes`, "");
        for (const f of m.fuentes) {
          const pagina = f.pagina ? `, p. ${f.pagina}` : "";
          const marca = f.usada ? "✓ citada" : "solo recuperada";
          lineas.push(`- [${f.indice}] **${f.documentoNombre}**${pagina} — _${marca}_`);
        }
        lineas.push("");
      }
      if (m.verificacion) {
        lineas.push(
          `> Verificación: ${
            m.verificacion.verdict === "supported"
              ? "respuesta sostenida por las fuentes"
              : m.verificacion.verdict === "unsupported"
                ? "⚠️ no sostenida" + (m.verificacion.critique ? ` — ${m.verificacion.critique}` : "")
                : "no completada"
          }`,
          "",
        );
      }
    }
  }
  return lineas.join("\n");
}

export function descargar(nombre: string, contenido: string): void {
  const blob = new Blob([contenido], { type: "text/markdown;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = nombre;
  a.click();
  URL.revokeObjectURL(url);
}
