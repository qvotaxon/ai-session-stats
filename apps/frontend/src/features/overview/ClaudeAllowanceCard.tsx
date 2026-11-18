import { useQuery } from "@tanstack/react-query";
import { QueryErrorAlert } from "@/common/QueryErrorAlert";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { overviewApi, type ClaudeAllowance } from "@/features/overview/api";
import { formatDateTime, formatPercent } from "@/lib/format";

type AllowanceWindowValue = NonNullable<ClaudeAllowance["fiveHour"]>;

const useClaudeAllowance = () =>
  useQuery({
    queryKey: ["claude", "allowance"],
    queryFn: ({ signal }) => overviewApi.claudeAllowance(signal),
    refetchInterval: 15_000,
  });

const AllowanceWindow = ({ label, value }: { label: string; value: AllowanceWindowValue }) => (
  <div className="space-y-1">
    <div className="flex items-center justify-between gap-2">
      <span>{label}</span>
      <span className="tabular-nums">{formatPercent((100 - value.usedPercentage) / 100)} remaining</span>
    </div>
    <progress
      className="h-2 w-full accent-primary"
      aria-label={`${label} remaining`}
      max={100}
      value={100 - value.usedPercentage}
    />
    <p className="text-xs text-muted-foreground">
      {value.resetsAt ? `Resets ${formatDateTime(value.resetsAt)}` : "Reset time unavailable"}
    </p>
  </div>
);

export const ClaudeAllowanceCard = () => {
  const query = useClaudeAllowance();

  return (
    <Card>
      <CardHeader>
        <CardTitle>Claude allowance</CardTitle>
        <CardDescription>Subscription usage limits reported by Claude Code</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3 text-sm">
        {query.isPending ? (
          <p role="status">Loading Claude allowance...</p>
        ) : query.isError ? (
          <QueryErrorAlert
            title="Could not load Claude allowance"
            error={query.error}
            onRetry={() => void query.refetch()}
          />
        ) : (
          <>
            {query.data.fiveHour && <AllowanceWindow label="5-hour limit" value={query.data.fiveHour} />}
            {query.data.sevenDay && <AllowanceWindow label="7-day limit" value={query.data.sevenDay} />}
            {query.data.message && <p className="text-muted-foreground">{query.data.message}</p>}
            {query.data.updatedAt && (
              <p className="text-xs text-muted-foreground">
                Last updated {formatDateTime(query.data.updatedAt)}
                {query.data.status === "stale" ? " · stale" : ""}
              </p>
            )}
            {query.data.status === "available" &&
              !query.data.message &&
              !query.data.fiveHour &&
              !query.data.sevenDay && <p className="text-muted-foreground">No subscription limits were reported.</p>}
          </>
        )}
      </CardContent>
    </Card>
  );
};
