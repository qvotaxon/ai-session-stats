import { Card, CardContent } from "@/components/ui/card";
import { formatCompact, formatInteger } from "@/lib/format";
import type { SessionSummary } from "../schemas";

interface SummaryStripProps {
  sessions: SessionSummary[];
  totalCount: number;
}

/** Derived from the page on screen only, so no extra request is needed. */
export const SummaryStrip = ({ sessions, totalCount }: SummaryStripProps) => {
  const tokens = sessions.reduce((sum, s) => sum + s.totalTokens, 0);
  const tools = sessions.reduce((sum, s) => sum + s.toolCallCount, 0);
  const items = [
    { label: "Sessions (matching)", value: formatInteger(totalCount) },
    { label: "Tokens on this page", value: formatCompact(tokens) },
    { label: "Tool calls on this page", value: formatInteger(tools) },
  ];
  return (
    <dl className="grid grid-cols-1 gap-3 sm:grid-cols-3">
      {items.map((item) => (
        <Card key={item.label} className="gap-1 py-3">
          <CardContent className="px-4">
            <dt className="text-xs text-muted-foreground">{item.label}</dt>
            <dd className="text-xl font-semibold tabular-nums">{item.value}</dd>
          </CardContent>
        </Card>
      ))}
    </dl>
  );
};
