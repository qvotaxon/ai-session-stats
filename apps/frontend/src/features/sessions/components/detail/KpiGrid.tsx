import type { ReactNode } from "react";
import { Card, CardContent } from "@/components/ui/card";
import { formatCompact, formatDurationMs, formatInteger, formatNanoAiu, formatPercent } from "@/lib/format";
import type { SessionDetail } from "../../schemas";

const Kpi = ({ label, value, hint }: { label: string; value: ReactNode; hint?: ReactNode }) => (
  <Card className="gap-1 py-4">
    <CardContent className="px-4">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-2xl font-semibold tabular-nums">{value}</dd>
      {hint && <p className="mt-1 text-xs text-muted-foreground">{hint}</p>}
    </CardContent>
  </Card>
);

export const KpiGrid = ({ session }: { session: SessionDetail }) => {
  const { tokens, toolCalls, hooks, guards, subagents, api, cost, codeChanges } = session;
  const failureRate = toolCalls.total > 0 ? toolCalls.failed / toolCalls.total : 0;

  const apiTimeNotRecorded = api.totalApiDurationMs === 0 && api.requestCount > 0;

  return (
    <dl className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-4" aria-label="Key metrics">
      <Kpi
        label="Total tokens"
        value={<span title={formatInteger(tokens.total)}>{formatCompact(tokens.total)}</span>}
      />
      <Kpi
        label="Input / output / cache"
        value={`${formatCompact(tokens.input)} / ${formatCompact(tokens.output)}`}
        hint={`Cache ${formatCompact(tokens.cacheRead)} read · ${formatCompact(tokens.cacheWrite)} write`}
      />
      <Kpi
        label="Tool calls"
        value={formatInteger(toolCalls.total)}
        hint={`${formatInteger(toolCalls.failed)} failed (${formatPercent(failureRate)})`}
      />
      <Kpi label="Hooks" value={formatInteger(hooks.total)} hint={`${formatInteger(hooks.failed)} failed`} />
      <Kpi
        label="Guards"
        value={formatInteger(guards.total)}
        hint={`${formatInteger(guards.blockedHooks)} blocked · ${formatInteger(guards.permissionDenials)} denied`}
      />
      <Kpi label="Subagents" value={formatInteger(subagents.total)} />
      <Kpi
        label="API time"
        value={
          apiTimeNotRecorded ? (
            <span className="text-base font-normal text-muted-foreground">Not recorded</span>
          ) : (
            formatDurationMs(api.totalApiDurationMs)
          )
        }
        hint={`${formatInteger(api.requestCount)} requests`}
      />
      {cost && (
        <Kpi
          label="Premium requests"
          value={formatInteger(cost.premiumRequests)}
          hint={
            <span title="AI units (AIU) consumed. 1 AIU = 1,000,000,000 nano AIU, as reported by the CLI.">
              {formatNanoAiu(cost.nanoAiu)} consumed
            </span>
          }
        />
      )}
      {codeChanges && (
        <Kpi
          label="Lines changed"
          value={
            <span>
              <span className="text-chart-5">+{formatInteger(codeChanges.linesAdded)}</span>{" "}
              <span className="text-destructive">−{formatInteger(codeChanges.linesRemoved)}</span>
            </span>
          }
          hint={`${formatInteger(codeChanges.filesModified)} files modified`}
        />
      )}
    </dl>
  );
};
