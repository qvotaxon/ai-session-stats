import { Cell, Pie, PieChart } from "recharts";
import { Badge } from "@/components/ui/badge";
import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatInteger } from "@/lib/format";
import type { SessionDetail } from "../../schemas";
import { ChartCard } from "./ChartCard";

const config = {
  blockedHook: { label: "Blocked by hook", color: "var(--chart-3)" },
  permissionDenial: { label: "Permission denied", color: "var(--chart-4)" },
  hookFailure: { label: "Hook failed", color: "var(--chart-failed)" },
} satisfies ChartConfig;

type Kind = keyof typeof config;

export const GuardsPanel = ({ guards }: { guards: SessionDetail["guards"] }) => {
  const totals: Array<{ kind: Kind; value: number }> = [
    { kind: "blockedHook", value: guards.blockedHooks },
    { kind: "permissionDenial", value: guards.permissionDenials },
    { kind: "hookFailure", value: guards.hookFailures },
  ];
  const data = totals.filter((t) => t.value > 0);
  const items = [...guards.items].sort((a, b) => b.count - a.count);

  return (
    <ChartCard
      title="Guards"
      description="Blocked hooks, permission denials and hook failures"
      summary={data.map((d) => `${config[d.kind].label} ${formatInteger(d.value)}`).join(", ")}
      empty={guards.total === 0 && items.length === 0}
      emptyMessage="No guards triggered in this session."
    >
      <div className="grid gap-4 md:grid-cols-2">
        <ChartContainer config={config} className="aspect-auto h-60 w-full">
          <PieChart>
            <ChartTooltip content={<ChartTooltipContent nameKey="kind" hideLabel />} />
            <Pie data={data} dataKey="value" nameKey="kind" innerRadius="50%" outerRadius="80%" paddingAngle={2}>
              {data.map((entry) => (
                <Cell key={entry.kind} fill={`var(--color-${entry.kind})`} />
              ))}
            </Pie>
            <ChartLegend content={<ChartLegendContent nameKey="kind" />} />
          </PieChart>
        </ChartContainer>
        <div className="max-h-60 overflow-auto rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Kind</TableHead>
                <TableHead>Name</TableHead>
                <TableHead className="text-right">Count</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((item) => (
                <TableRow key={`${item.kind}:${item.name}`}>
                  <TableCell>
                    <Badge variant="outline">{config[item.kind].label}</Badge>
                  </TableCell>
                  <TableCell className="max-w-[10rem] truncate">{item.name}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatInteger(item.count)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </div>
    </ChartCard>
  );
};
