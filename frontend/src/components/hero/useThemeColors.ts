import { useEffect, useState } from "react";
import * as THREE from "three";

function readColors(): {
  accent: THREE.Color;
  highlight: THREE.Color;
  bg: THREE.Color;
  isDark: boolean;
} {
  if (typeof window === "undefined") {
    return {
      accent: new THREE.Color("#2e7d6e"),
      highlight: new THREE.Color("#a34a2a"),
      bg: new THREE.Color("#eef0e9"),
      isDark: false,
    };
  }
  const isDark = document.documentElement.getAttribute("data-theme") === "dark";
  const styles = getComputedStyle(document.documentElement);
  const read = (varName: string, fallback: string) =>
    new THREE.Color(styles.getPropertyValue(varName).trim() || fallback);
  return {
    accent: read("--accent", "#2e7d6e"),
    highlight: read("--highlight", "#a34a2a"),
    bg: read("--bg", "#eef0e9"),
    isDark,
  };
}

export function useThemeColors() {
  const [colors, setColors] = useState(readColors);

  useEffect(() => {
    const update = () => setColors(readColors());
    update();
    const observer = new MutationObserver(update);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    return () => observer.disconnect();
  }, []);

  return colors;
}