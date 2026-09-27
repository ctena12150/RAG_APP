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
      accent: new THREE.Color("#cf2038"),
      highlight: new THREE.Color("#b5823e"),
      bg: new THREE.Color("#f7f1e6"),
      isDark: false,
    };
  }
  const isDark = document.documentElement.getAttribute("data-theme") === "dark";
  const styles = getComputedStyle(document.documentElement);
  const read = (varName: string, fallback: string) =>
    new THREE.Color(styles.getPropertyValue(varName).trim() || fallback);
  return {
    accent: read("--accent", "#cf2038"),
    highlight: read("--highlight", "#b5823e"),
    bg: read("--bg", "#f7f1e6"),
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