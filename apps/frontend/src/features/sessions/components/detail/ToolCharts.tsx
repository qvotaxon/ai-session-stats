import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from "recharts";
import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart";
import { formatDurationMs, formatInteger } from "@/lib/format";
import type { SessionDetail } from "../../schemas";
import { ChartCard } from "./ChartCard";

const TOP = 15;

const countConfig = {
  succeeded: { label: "Succeeded", color: "var(--chart-1)" },
  failed: { label: "Failed", color: "var(--chart-failed)" },
} satisfies ChartConfig;

const durationConfig = { avgDurationMs: { label: "Avg duration", color: "var(--chart-2)" } } satisfies ChartConfig;

const height = (rows: number) => rows * 30 + 70;

export const ToolCallsChart = ({ tools }: { tools: SessionDetail["toolCalls"]["byTool"] }) => {
  const data = [...tools]
    .sort((a, b) => b.count - a.count)
    .slice(0, TOP)
    .map((t) => ({ name: t.name, succeeded: Math.max(t.count - t.failed, 0), failed: t.failed }));

  return (
    <ChartCard
      title="Tool calls by tool"
      description={`Top ${TOP} by call count; failures highlighted`}
      summary={data
        .map((d) => `${d.name}: ${formatInteger(d.succeeded + d.failed)} calls, ${formatInteger(d.failed)} failed`)
        .join("; ")}
      empty={data.length === 0}
    >
      <ChartContainer config={countConfig} className="aspect-auto w-full" style={{ height: height(data.length) }}>
        <BarChart data={data} layout="vertical" margin={{ left: 8 }}>
          <CartesianGrid horizontal={false} />
          <YAxis dataKey="name" type="category" width={110} tickLine={false} axisLine={false} tick={{ fontSize: 12 }} />
          <XAxis type="number" allowDecimals={false} />
          <ChartTooltip content={<ChartTooltipContent />} />
          <ChartLegend content={<ChartLegendContent />} />
          <Bar dataKey="succeeded" stackId="calls" fill="var(--color-succeeded)" />
          <Bar dataKey="failed" stackId="calls" fill="var(--color-failed)" />
        </BarChart>
      </ChartContainer>
    </ChartCard>
  );
};

export const ToolDurationChart = ({ tools }: { tools: SessionDetail["toolCalls"]["byTool"] }) => {
  const data = [...tools]
    .filter((t) => t.avgDurationMs > 0)
    .sort((a, b) => b.avgDurationMs - a.avgDurationMs)
    .slice(0, TOP)
    .map((t) => ({ name: t.name, avgDurationMs: Math.round(t.avgDurationMs) }));

  return (
    <ChartCard
      title="Average duration by tool"
      description={`Slowest ${TOP} tools`}
      summary={data.map((d) => `${d.name}: ${formatDurationMs(d.avgDurationMs)}`).join("; ")}
      empty={data.length === 0}
    >
      <ChartContainer config={durationConfig} className="aspect-auto w-full" style={{ height: height(data.length) }}>
        <BarChart data={data} layout="vertical" margin={{ left: 8 }}>
          <CartesianGrid horizontal={false} />
          <YAxis dataKey="name" type="category" width={110} tickLine={false} axisLine={false} tick={{ fontSize: 12 }} />
          <XAxis type="number" tickFormatter={formatDurationMs} />
          <ChartTooltip content={<ChartTooltipContent formatter={(v) => formatDurationMs(Number(v))} />} />
          <Bar dataKey="avgDurationMs" fill="var(--color-avgDurationMs)" radius={3} />
        </BarChart>
      </ChartContainer>
    </ChartCard>
  );
};
