import { formatInteger } from "@/lib/format";
import { ChartCard } from "./ChartCard";

interface NameCount {
  name: string;
  count: number;
}

interface CountListCardProps {
  title: string;
  description: string;
  items: NameCount[];
  emptyMessage: string;
}

/** Ranked list with proportional bars: plain markup, so it is keyboard- and screen-reader-friendly. */
export const CountListCard = ({ title, description, items, emptyMessage }: CountListCardProps) => {
  const sorted = [...items].sort((a, b) => b.count - a.count);
  const max = Math.max(...sorted.map((i) => i.count), 1);
  return (
    <ChartCard
      title={title}
      description={description}
      summary={sorted.map((i) => `${i.name} ${formatInteger(i.count)}`).join(", ")}
      empty={sorted.length === 0}
      emptyMessage={emptyMessage}
    >
      <ul className="space-y-2">
        {sorted.map((item) => (
          <li key={item.name} className="space-y-1">
            <div className="flex justify-between gap-2 text-sm">
              <span className="truncate">{item.name}</span>
              <span className="tabular-nums text-muted-foreground">{formatInteger(item.count)}</span>
            </div>
            <div className="h-1.5 rounded-full bg-muted" aria-hidden>
              <div className="h-full rounded-full bg-chart-1" style={{ width: `${(item.count / max) * 100}%` }} />
            </div>
          </li>
        ))}
      </ul>
    </ChartCard>
  );
};
