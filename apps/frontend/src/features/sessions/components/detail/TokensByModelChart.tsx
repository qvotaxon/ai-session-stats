import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from "recharts";
import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart";
import { formatCompact, formatInteger } from "@/lib/format";
import type { SessionDetail } from "../../schemas";
import { ChartCard } from "./ChartCard";

const config = {
  input: { label: "Input", color: "var(--chart-1)" },
  output: { label: "Output", color: "var(--chart-2)" },
  cacheRead: { label: "Cache read", color: "var(--chart-3)" },
  cacheWrite: { label: "Cache write", color: "var(--chart-4)" },
  reasoning: { label: "Reasoning", color: "var(--chart-5)" },
} satisfies ChartConfig;

export const TokensByModelChart = ({ models }: { models: SessionDetail["tokensByModel"] }) => (
  <ChartCard
    title="Tokens by model"
    description="Stacked by token type"
    summary={models
      .map(
        (m) =>
          `${m.model}: ${formatInteger(m.input + m.output + m.cacheRead + m.cacheWrite + m.reasoning)} tokens over ${formatInteger(m.requests)} requests`,
      )
      .join("; ")}
    empty={models.length === 0}
  >
    <ChartContainer config={config} className="aspect-auto w-full" style={{ height: models.length * 44 + 80 }}>
      <BarChart data={models} layout="vertical" margin={{ left: 8 }}>
        <CartesianGrid horizontal={false} />
        <YAxis dataKey="model" type="category" width={110} tickLine={false} axisLine={false} tick={{ fontSize: 12 }} />
        <XAxis type="number" tickFormatter={formatCompact} />
        <ChartTooltip
          content={
            <ChartTooltipContent
              formatter={(v, name) =>
                `${config[name as keyof typeof config]?.label ?? name}: ${formatCompact(Number(v))}`
              }
            />
          }
        />
        <ChartLegend content={<ChartLegendContent />} />
        {(Object.keys(config) as Array<keyof typeof config>).map((key) => (
          <Bar key={key} dataKey={key} stackId="tokens" fill={`var(--color-${key})`} />
        ))}
      </BarChart>
    </ChartContainer>
  </ChartCard>
);
