export function AmbientBackground() {
  return (
    <div className="pointer-events-none fixed inset-0 z-0 overflow-hidden" aria-hidden="true">
      <div
        className="ambient-blob absolute left-[10%] top-[-10%] h-[60vmax] w-[60vmax] rounded-full opacity-[0.10] dark:opacity-[0.16]"
        style={{
          background: 'radial-gradient(circle, var(--accent) 0%, transparent 70%)',
          filter: 'blur(60px)',
          animation: 'drift-a 46s ease-in-out infinite',
        }}
      />
      <div
        className="ambient-blob absolute bottom-[-15%] right-[5%] h-[55vmax] w-[55vmax] rounded-full opacity-[0.08] dark:opacity-[0.14]"
        style={{
          background: 'radial-gradient(circle, var(--highlight) 0%, transparent 70%)',
          filter: 'blur(70px)',
          animation: 'drift-b 55s ease-in-out infinite',
        }}
      />
    </div>
  );
}