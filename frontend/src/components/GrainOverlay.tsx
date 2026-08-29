export function GrainOverlay() {
  return (
    <svg
      className="grain-overlay pointer-events-none fixed inset-0 z-[9999] h-full w-full opacity-[0.025] mix-blend-overlay"
      aria-hidden="true"
    >
      <filter id="grain-noise">
        <feTurbulence type="fractalNoise" baseFrequency="0.85" numOctaves={2} stitchTiles="stitch" />
      </filter>
      <rect width="100%" height="100%" filter="url(#grain-noise)" />
    </svg>
  );
}