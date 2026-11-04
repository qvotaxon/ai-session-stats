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

const config = {
  succeeded: { label: "Succeeded", color: "var(--chart-1)" },
  failed: { label: "Failed", color: "var(--chart-failed)" },
} satisfies ChartConfig;

export const HooksChart = ({ hooks }: { hooks: SessionDetail["hooks"] }) => {
  const data = hooks.byType.map((h) => ({
    type: h.type,
    succeeded: Math.max(h.count - h.failed, 0),
    failed: h.failed,
    avgDurationMs: h.avgDurationMs,
  }));

  return (
    <ChartCard
      title="Hooks by type"
      description="Success vs failure, with average duration"
      summary={data
        .map(
          (d) =>
            `${d.type}: ${formatInteger(d.succeeded + d.failed)} runs, ${formatInteger(d.failed)} failed, average ${formatDurationMs(d.avgDurationMs)}`,
        )
        .join("; ")}
      empty={data.length === 0}
      emptyMessage="No hooks ran in this session."
    >
      <ChartContainer config={config} className="aspect-auto w-full" style={{ height: data.length * 44 + 80 }}>
        <BarChart data={data} layout="vertical" margin={{ left: 8 }}>
          <CartesianGrid horizontal={false} />
          <YAxis dataKey="type" type="category" width={110} tickLine={false} axisLine={false} tick={{ fontSize: 12 }} />
          <XAxis type="number" allowDecimals={false} />
          <ChartTooltip content={<ChartTooltipContent />} />
          <ChartLegend content={<ChartLegendContent />} />
          <Bar dataKey="succeeded" stackId="hooks" fill="var(--color-succeeded)" />
          <Bar dataKey="failed" stackId="hooks" fill="var(--color-failed)" />
        </BarChart>
      </ChartContainer>
      <ul className="mt-3 space-y-1 text-xs text-muted-foreground">
        {data.map((d) => (
          <li key={d.type} className="flex justify-between gap-2">
            <span className="truncate">{d.type}</span>
            <span>avg {formatDurationMs(d.avgDurationMs)}</span>
          </li>
        ))}
      </ul>
    </ChartCard>
  );
};
