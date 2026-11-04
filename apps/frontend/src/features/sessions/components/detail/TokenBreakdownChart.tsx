import { Cell, Pie, PieChart } from "recharts";
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

export const TokenBreakdownChart = ({ tokens }: { tokens: SessionDetail["tokens"] }) => {
  const data = (Object.keys(config) as Array<keyof typeof config>)
    .map((key) => ({ key, label: config[key].label, value: tokens[key] }))
    .filter((entry) => entry.value > 0);

  return (
    <ChartCard
      title="Token breakdown"
      description="Share of each token type"
      summary={
        data.length === 0
          ? "No tokens."
          : data.map((d) => `${d.label} ${formatInteger(d.value)}`).join(", ") +
            `. Total ${formatInteger(tokens.total)}.`
      }
      empty={data.length === 0}
    >
      <ChartContainer config={config} className="aspect-auto h-72 w-full">
        <PieChart>
          <ChartTooltip
            content={<ChartTooltipContent nameKey="key" hideLabel formatter={(v) => formatCompact(Number(v))} />}
          />
          <Pie data={data} dataKey="value" nameKey="key" innerRadius="55%" outerRadius="80%" paddingAngle={2}>
            {data.map((entry) => (
              <Cell key={entry.key} fill={`var(--color-${entry.key})`} />
            ))}
          </Pie>
          <ChartLegend content={<ChartLegendContent nameKey="key" />} />
        </PieChart>
      </ChartContainer>
    </ChartCard>
  );
};
