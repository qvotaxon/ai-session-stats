import { Area, AreaChart, CartesianGrid, XAxis, YAxis } from "recharts";
import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { formatCompact, formatDateTime, formatInteger, formatTime } from "@/lib/format";
import type { SessionDetail } from "../../schemas";
import { ChartCard } from "./ChartCard";

const activityConfig = {
  toolCalls: { label: "Tool calls", color: "var(--chart-1)" },
  hooks: { label: "Hooks", color: "var(--chart-2)" },
  guards: { label: "Guards", color: "var(--chart-3)" },
} satisfies ChartConfig;

const tokensConfig = { tokens: { label: "Tokens", color: "var(--chart-4)" } } satisfies ChartConfig;

export const TimelineChart = ({ timeline }: { timeline: SessionDetail["timeline"] }) => {
  const totalTools = timeline.reduce((sum, b) => sum + b.toolCalls, 0);
  const totalTokens = timeline.reduce((sum, b) => sum + b.tokens, 0);
  const first = timeline[0];
  const last = timeline[timeline.length - 1];

  return (
    <ChartCard
      title="Activity timeline"
      description="Activity across the session, in equal time buckets"
      summary={
        first && last
          ? `From ${formatDateTime(first.bucketStart)} to ${formatDateTime(last.bucketStart)}: ${formatInteger(totalTools)} tool calls and ${formatCompact(totalTokens)} tokens.`
          : ""
      }
      empty={timeline.length === 0}
      emptyMessage="No timeline available."
      className="lg:col-span-2"
    >
      <Tabs defaultValue="activity">
        <TabsList>
          <TabsTrigger value="activity">Activity</TabsTrigger>
          <TabsTrigger value="tokens">Tokens</TabsTrigger>
        </TabsList>
        <TabsContent value="activity">
          <ChartContainer config={activityConfig} className="aspect-auto h-72 w-full">
            <AreaChart data={timeline}>
              <CartesianGrid vertical={false} />
              <XAxis dataKey="bucketStart" tickFormatter={formatTime} minTickGap={32} tickLine={false} />
              <YAxis allowDecimals={false} width={32} />
              <ChartTooltip
                content={<ChartTooltipContent labelFormatter={(label) => formatDateTime(String(label))} />}
              />
              <ChartLegend content={<ChartLegendContent />} />
              {(Object.keys(activityConfig) as Array<keyof typeof activityConfig>).map((key) => (
                <Area
                  key={key}
                  dataKey={key}
                  type="monotone"
                  stackId="a"
                  stroke={`var(--color-${key})`}
                  fill={`var(--color-${key})`}
                  fillOpacity={0.35}
                />
              ))}
            </AreaChart>
          </ChartContainer>
        </TabsContent>
        <TabsContent value="tokens">
          <ChartContainer config={tokensConfig} className="aspect-auto h-72 w-full">
            <AreaChart data={timeline}>
              <CartesianGrid vertical={false} />
              <XAxis dataKey="bucketStart" tickFormatter={formatTime} minTickGap={32} tickLine={false} />
              <YAxis tickFormatter={formatCompact} width={44} />
              <ChartTooltip
                content={<ChartTooltipContent labelFormatter={(label) => formatDateTime(String(label))} />}
              />
              <Area
                dataKey="tokens"
                type="monotone"
                stroke="var(--color-tokens)"
                fill="var(--color-tokens)"
                fillOpacity={0.35}
              />
            </AreaChart>
          </ChartContainer>
        </TabsContent>
      </Tabs>
    </ChartCard>
  );
};
