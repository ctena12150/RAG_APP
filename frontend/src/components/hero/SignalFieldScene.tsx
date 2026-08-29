import { useEffect, useMemo, useRef } from "react";
import { Canvas, useFrame } from "@react-three/fiber";
import { EffectComposer, Bloom } from "@react-three/postprocessing";
import * as THREE from "three";
import { useThemeColors } from "./useThemeColors";

const PARTICLE_COUNT = 180;
const GROUP_COUNT = 24;

function makeDotTexture(): THREE.Texture {
  const size = 64;
  const canvas = document.createElement("canvas");
  canvas.width = size;
  canvas.height = size;
  const ctx = canvas.getContext("2d");
  if (ctx) {
    const gradient = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    gradient.addColorStop(0, "rgba(255,255,255,1)");
    gradient.addColorStop(0.4, "rgba(255,255,255,0.65)");
    gradient.addColorStop(1, "rgba(255,255,255,0)");
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, size, size);
  }
  const texture = new THREE.CanvasTexture(canvas);
  texture.needsUpdate = true;
  return texture;
}

function Terrain({
  accent,
  highlight,
  bg,
  reduceMotion,
  isDark,
}: {
  accent: THREE.Color;
  highlight: THREE.Color;
  bg: THREE.Color;
  reduceMotion: boolean;
  isDark: boolean;
}) {
  const geoRef = useRef<THREE.PlaneGeometry>(null);
  const basePositions = useRef<Float32Array | null>(null);

  useEffect(() => {
    const geo = geoRef.current;
    if (!geo) return;
    basePositions.current = Float32Array.from(geo.attributes.position.array as ArrayLike<number>);
    const count = geo.attributes.position.count;
    geo.setAttribute("color", new THREE.BufferAttribute(new Float32Array(count * 3), 3));
  }, []);

  useFrame((state) => {
    const geo = geoRef.current;
    const base = basePositions.current;
    if (!geo || !base) return;
    const t = reduceMotion ? 0 : state.clock.elapsedTime;
    const posAttr = geo.attributes.position as THREE.BufferAttribute;
    const colorAttr = geo.attributes.color as THREE.BufferAttribute;

    const dimMix = isDark ? 0.07 : 0.22;
    const peakMix = isDark ? 0.42 : 1.0;
    const dimR = bg.r + (accent.r - bg.r) * dimMix;
    const dimG = bg.g + (accent.g - bg.g) * dimMix;
    const dimB = bg.b + (accent.b - bg.b) * dimMix;
    const peakR = bg.r + (accent.r - bg.r) * peakMix;
    const peakG = bg.g + (accent.g - bg.g) * peakMix;
    const peakB = bg.b + (accent.b - bg.b) * peakMix;
    const glintMix = isDark ? 0.22 : 0.35;

    for (let i = 0; i < posAttr.count; i++) {
      const ix = i * 3;
      const x = base[ix];
      const y = base[ix + 1];
      const swell = Math.sin(x * 0.08 + t * 0.25) * 0.8 + Math.cos(y * 0.1 - t * 0.22) * 0.6;
      const ripple = Math.sin(x * 0.18 + y * 0.14 + t * 0.7) * 0.5 + Math.sin(x * 0.22 - y * 0.16 - t * 0.5) * 0.4;
      const wave = swell + ripple;
      posAttr.setZ(i, wave * 1.4);

      const e = Math.max(0, Math.min(1, (wave + 2.2) / 4.4));
      let r = dimR + (peakR - dimR) * e;
      let g = dimG + (peakG - dimG) * e;
      let b = dimB + (peakB - dimB) * e;
      if (e > 0.82) {
        const glintT = ((e - 0.82) / 0.18) * glintMix;
        r += (highlight.r - r) * glintT;
        g += (highlight.g - g) * glintT;
        b += (highlight.b - b) * glintT;
      }
      colorAttr.setXYZ(i, r, g, b);
    }
    posAttr.needsUpdate = true;
    colorAttr.needsUpdate = true;
  });

  return (
    <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, -1.5, -5]}>
      <planeGeometry ref={geoRef} args={[200, 140, 100, 100]} />
      <meshBasicMaterial vertexColors wireframe transparent opacity={isDark ? 0.3 : 0.5} />
    </mesh>
  );
}

function ParticleField({
  accent,
  highlight,
  bg,
  reduceMotion,
  isDark,
}: {
  accent: THREE.Color;
  highlight: THREE.Color;
  bg: THREE.Color;
  reduceMotion: boolean;
  isDark: boolean;
}) {
  const pointsRef = useRef<THREE.Points>(null);
  const dotTexture = useMemo(() => makeDotTexture(), []);

  const { positions, groups } = useMemo(() => {
    const positions = new Float32Array(PARTICLE_COUNT * 3);
    for (let i = 0; i < PARTICLE_COUNT; i++) {
      const angle = Math.random() * Math.PI * 2;
      const radius = 5 + Math.random() * 18;
      positions[i * 3] = Math.cos(angle) * radius;
      positions[i * 3 + 1] = (Math.random() - 0.3) * 14 + 1;
      positions[i * 3 + 2] = -4 - Math.random() * 18;
    }
    const order = Array.from({ length: PARTICLE_COUNT }, (_, i) => i);
    for (let i = order.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [order[i], order[j]] = [order[j], order[i]];
    }
    const groups: number[][] = Array.from({ length: GROUP_COUNT }, () => []);
    order.forEach((idx, n) => groups[n % GROUP_COUNT].push(idx));
    return { positions, groups };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const colorAttr = useMemo(() => new Float32Array(PARTICLE_COUNT * 3), []);
  const groupActivity = useRef<Float32Array>(new Float32Array(GROUP_COUNT));
  const groupColorRefs = useMemo(() => Array.from({ length: GROUP_COUNT }, () => accent.clone()), [accent]);
  const cycle = useRef({
    activeGroup: -1,
    phase: "idle" as "idle" | "rising" | "holding" | "falling",
    phaseStart: 0,
    nextTrigger: 1.5,
  });

  useFrame((state) => {
    const t = state.clock.elapsedTime;
    const c = cycle.current;

    if (!reduceMotion) {
      if (c.phase === "idle" && t >= c.nextTrigger) {
        c.activeGroup = Math.floor(Math.random() * GROUP_COUNT);
        c.phase = "rising";
        c.phaseStart = t;
      } else if (c.phase === "rising" && t - c.phaseStart > 0.7) {
        c.phase = "holding";
        c.phaseStart = t;
      } else if (c.phase === "holding" && t - c.phaseStart > 1.0) {
        c.phase = "falling";
        c.phaseStart = t;
      } else if (c.phase === "falling" && t - c.phaseStart > 1.1) {
        c.phase = "idle";
        c.activeGroup = -1;
        c.nextTrigger = t + 2.5 + Math.random() * 3.5;
      }
    }

    const target = c.phase === "rising" || c.phase === "holding" ? 1 : 0;
    for (let g = 0; g < GROUP_COUNT; g++) {
      const goal = g === c.activeGroup ? target : 0;
      groupActivity.current[g] += (goal - groupActivity.current[g]) * 0.07;
      const towardHighlight = g === c.activeGroup && c.phase === "holding" ? 0.45 : 0;
      groupColorRefs[g].copy(accent).lerp(highlight, towardHighlight);
    }

    if (pointsRef.current) {
      pointsRef.current.rotation.y = reduceMotion ? 0 : Math.sin(t * 0.03) * 0.09;
      pointsRef.current.rotation.x = reduceMotion ? 0 : Math.cos(t * 0.025) * 0.03;
    }

    groups.forEach((idxs, g) => {
      const a = groupActivity.current[g];
      const boost = 1 + a * 0.9;
      const idleMix = isDark ? 0.06 : 0.16;
      const peakMix = isDark ? 0.82 : 0.84;
      const mixed = idleMix + a * peakMix;
      idxs.forEach((idx) => {
        colorAttr[idx * 3] = (bg.r + (groupColorRefs[g].r - bg.r) * mixed) * boost;
        colorAttr[idx * 3 + 1] = (bg.g + (groupColorRefs[g].g - bg.g) * mixed) * boost;
        colorAttr[idx * 3 + 2] = (bg.b + (groupColorRefs[g].b - bg.b) * mixed) * boost;
      });
    });

    if (pointsRef.current) {
      const attr = pointsRef.current.geometry.attributes.color as THREE.BufferAttribute | undefined;
      if (attr) attr.needsUpdate = true;
    }
  });

  return (
    <points ref={pointsRef}>
      <bufferGeometry>
        <bufferAttribute attach="attributes-position" args={[positions, 3]} />
        <bufferAttribute attach="attributes-color" args={[colorAttr, 3]} />
      </bufferGeometry>
      <pointsMaterial
        size={0.18}
        map={dotTexture}
        vertexColors
        transparent
        opacity={0.9}
        depthWrite={false}
        blending={THREE.AdditiveBlending}
        sizeAttenuation
      />
    </points>
  );
}

function CameraRig({ reduceMotion }: { reduceMotion: boolean }) {
  useFrame((state) => {
    if (reduceMotion) return;
    const { pointer, camera } = state;
    camera.position.x += (pointer.x * 1.5 - camera.position.x) * 0.02;
    camera.position.y += (0.5 - pointer.y * 0.3 - camera.position.y) * 0.02;
    camera.lookAt(0, 0, -5);
  });
  return null;
}

export function SignalFieldScene({ reduceMotion, paused }: { reduceMotion: boolean; paused: boolean }) {
  const { accent, highlight, bg, isDark } = useThemeColors();

  return (
    <Canvas
      dpr={[1, 1.5]}
      gl={{ antialias: true, alpha: true, powerPreference: "low-power" }}
      camera={{ position: [0, 0, 5], fov: 65, near: 0.1, far: 100 }}
      frameloop={paused ? "never" : "always"}
    >
      <fog attach="fog" args={[bg, 1, 60]} />
      <Terrain accent={accent} highlight={highlight} bg={bg} reduceMotion={reduceMotion} isDark={isDark} />
      <ParticleField accent={accent} highlight={highlight} bg={bg} reduceMotion={reduceMotion} isDark={isDark} />
      <CameraRig reduceMotion={reduceMotion} />
      <EffectComposer>
        <Bloom luminanceThreshold={0.5} luminanceSmoothing={0.85} intensity={0.85} />
      </EffectComposer>
    </Canvas>
  );
}