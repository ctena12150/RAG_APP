import logo from "../assets/BM-logo.png";

/** Lockup Bleckmann sobre chip claro: el wordmark es negro, así se lee en ambos temas. */
export function LogoBleckmann({ height = 40, className }: { height?: number; className?: string }) {
  return (
    <span
      className={`inline-flex items-center justify-center rounded-lg ${className ?? ""}`}
      style={{ background: "#fdfaf3", border: "1px solid rgba(0,0,0,0.12)", padding: "6px 10px" }}
    >
      <img src={logo} alt="Bleckmann" style={{ height, width: "auto", display: "block" }} />
    </span>
  );
}
