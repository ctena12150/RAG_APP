import { useState, useEffect } from "react";
import { SceneErrorBoundary } from "./SignalErrorBoundary";
import { SignalFieldScene } from "./SignalFieldScene";

export default function SignalBackground({ reduceMotion }: { reduceMotion: boolean }) {
  const [hidden, setHidden] = useState(false);

  useEffect(() => {
    const onVisibility = () => setHidden(document.hidden);
    document.addEventListener("visibilitychange", onVisibility);
    return () => document.removeEventListener("visibilitychange", onVisibility);
  }, []);

  if (reduceMotion) return null;

  return (
    <div className="fixed inset-0 z-0 pointer-events-none">
      <SceneErrorBoundary>
        <SignalFieldScene reduceMotion={reduceMotion} paused={hidden} />
      </SceneErrorBoundary>
    </div>
  );
}