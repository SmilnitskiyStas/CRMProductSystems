"use client";

import { useState } from "react";
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { useLatestTemperatures, useTemperatureReadings } from "../hooks/useIot";
import type { IotDeviceDto } from "../types";

interface Props {
  storeId: string;
  devices: IotDeviceDto[];
}

export function TemperaturePanel({ storeId, devices }: Props) {
  const tempSensors = devices.filter((d) => d.deviceType === "temp_sensor" && d.isActive);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const activeId = selectedId ?? tempSensors[0]?.id ?? null;

  const { data: latest } = useLatestTemperatures(storeId);
  const { data: readings, isLoading: readingsLoading } = useTemperatureReadings(activeId);

  if (tempSensors.length === 0) {
    return (
      <div style={{ background: "#161B26", border: "1px solid #1F2937", borderRadius: 12, padding: 24, color: "#4B5563", fontSize: 13, textAlign: "center" }}>
        Немає активних датчиків температури
      </div>
    );
  }

  const chartData = (readings ?? [])
    .slice()
    .reverse()
    .map((r) => ({
      time: new Date(r.recordedAt).toLocaleTimeString("uk-UA", { hour: "2-digit", minute: "2-digit" }),
      temp: r.temperature,
    }));

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      {/* Latest readings cards */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(180px, 1fr))", gap: 12 }}>
        {tempSensors.map((d) => {
          const reading = latest?.find((t) => t.deviceId === d.id);
          const alert = reading?.isAlert ?? false;
          const selected = d.id === activeId;
          return (
            <button
              key={d.id}
              onClick={() => setSelectedId(d.id)}
              style={{
                background: alert ? "#2a0a0a" : "#161B26",
                border: `1px solid ${selected ? "#3B82F6" : alert ? "#991b1b" : "#1F2937"}`,
                borderRadius: 10,
                padding: "12px 14px",
                textAlign: "left",
                cursor: "pointer",
              }}
            >
              <div style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                🌡️ {d.name ?? d.deviceId}
              </div>
              <div style={{ color: alert ? "#ef4444" : "#E8EDF5", fontSize: 22, fontWeight: 700, fontFamily: "monospace" }}>
                {reading ? `${reading.temperature}°C` : "—"}
              </div>
              {reading?.humidity != null && (
                <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>вологість {reading.humidity}%</div>
              )}
              {alert && <div style={{ color: "#ef4444", fontSize: 11, fontWeight: 600, marginTop: 4 }}>⚠ ПЕРЕВИЩЕННЯ</div>}
            </button>
          );
        })}
      </div>

      {/* 24h chart for the selected sensor */}
      <div style={{ background: "#161B26", border: "1px solid #1F2937", borderRadius: 12, padding: 20 }}>
        <h3 style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, margin: "0 0 16px 0" }}>
          Температура за 24 години
        </h3>
        {readingsLoading ? (
          <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: 32 }}>Завантаження…</div>
        ) : chartData.length === 0 ? (
          <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: 32 }}>Немає показників</div>
        ) : (
          <ResponsiveContainer width="100%" height={260}>
            <LineChart data={chartData} margin={{ top: 4, right: 12, bottom: 0, left: -16 }}>
              <CartesianGrid stroke="#1F2937" strokeDasharray="3 3" />
              <XAxis dataKey="time" stroke="#4B5563" fontSize={11} minTickGap={40} />
              <YAxis stroke="#4B5563" fontSize={11} unit="°" />
              <Tooltip
                contentStyle={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, fontSize: 12 }}
                labelStyle={{ color: "#9CA3AF" }}
              />
              <Line type="monotone" dataKey="temp" name="°C" stroke="#3B82F6" strokeWidth={2} dot={false} />
            </LineChart>
          </ResponsiveContainer>
        )}
      </div>
    </div>
  );
}
