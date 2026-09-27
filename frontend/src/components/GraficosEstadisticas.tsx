import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { EstadisticasResumen } from "../lib/types";

export default function GraficosEstadisticas({ datos }: { datos: EstadisticasResumen }) {
  const colorLinea = "var(--accent-a)";
  if (datos.porDominio.length === 0 && datos.porDia.length === 0) return null;
  return (
    <div className="mt-4 grid gap-3 md:grid-cols-2">
      {datos.porDominio.length > 0 && (
        <div className="rounded-lg px-2 py-2" style={{ background: "var(--bg)", border: "1px solid var(--line)" }}>
          <h3 className="px-1 text-xs font-semibold">Consultas por dominio</h3>
          <div className="h-44">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={datos.porDominio} margin={{ top: 8, right: 8, left: -18, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--line)" />
                <XAxis dataKey="dominio" tick={{ fontSize: 10 }} interval={0} angle={-18} dy={8} height={44} />
                <YAxis tick={{ fontSize: 10 }} allowDecimals={false} />
                <Tooltip />
                <Bar dataKey="consultas" fill={colorLinea} radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      )}
      {datos.porDia.length > 0 && (
        <div className="rounded-lg px-2 py-2" style={{ background: "var(--bg)", border: "1px solid var(--line)" }}>
          <h3 className="px-1 text-xs font-semibold">Consultas por día</h3>
          <div className="h-44">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={datos.porDia} margin={{ top: 8, right: 8, left: -18, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--line)" />
                <XAxis dataKey="dia" tick={{ fontSize: 10 }} minTickGap={24} />
                <YAxis tick={{ fontSize: 10 }} allowDecimals={false} />
                <Tooltip />
                <Area type="monotone" dataKey="consultas" stroke={colorLinea} fill={colorLinea} fillOpacity={0.25} />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </div>
      )}
    </div>
  );
}
