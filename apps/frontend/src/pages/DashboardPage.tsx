import { Link } from "@tanstack/react-router";
import { Activity, ArrowRight, ShieldCheck } from "lucide-react";
import { QueryErrorAlert } from "@/common/QueryErrorAlert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { HarnessInstallations } from "@/features/overview/HarnessInstallations";
import { useOverview } from "@/features/overview/api";
import { SourceBadge } from "@/features/sessions/components/SourceBadge";
import { DEFAULT_SESSIONS_SEARCH } from "@/features/sessions/searchParams";
import { formatCompact, formatDateTime, formatInteger } from "@/lib/format";

const DashboardPage = () => {
  const query = useOverview();
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Dashboard</h1>
        <p className="text-sm text-muted-foreground">
          Recent work and the health of your MSDC harness. Analysis stays on this machine.
        </p>
      </div>
      {query.isPending ? (
        <p role="status">Loading dashboard...</p>
      ) : query.isError ? (
        <QueryErrorAlert title="Could not load dashboard" error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <>
          <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {[
              ["Active in the last 7 days", formatInteger(query.data.recentlyActiveSessions)],
              ["Sessions · all indexed history", formatInteger(query.data.totalSessions)],
              ["Recorded tokens · all history", formatCompact(query.data.totalTokens)],
              [
                "Tool failures · all history",
                `${formatInteger(query.data.toolFailures)} / ${formatInteger(query.data.toolCalls)}`,
              ],
            ].map(([label, value]) => (
              <Card key={label} className="py-4">
                <CardContent>
                  <dt className="text-xs text-muted-foreground">{label}</dt>
                  <dd className="mt-1 text-2xl font-semibold tabular-nums">{value}</dd>
                </CardContent>
              </Card>
            ))}
          </dl>
          <div className="grid items-start gap-4 lg:grid-cols-3">
            <Card className="lg:col-span-2">
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <Activity className="size-4" aria-hidden /> Recently active sessions
                </CardTitle>
                <CardDescription>The last three sessions by activity, including resumed sessions.</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                {query.data.recentSessions.length === 0 ? (
                  <p className="text-sm text-muted-foreground">
                    No sessions found. Local Claude and Copilot sessions will appear here.
                  </p>
                ) : (
                  query.data.recentSessions.map((session) => (
                    <div key={session.id} className="space-y-1 border-b pb-3 last:border-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <SourceBadge source={session.source} />
                        <Link
                          to="/sessions/$sessionId"
                          params={{ sessionId: session.id }}
                          className="font-medium hover:underline"
                        >
                          {session.title || session.id}
                        </Link>
                      </div>
                      <p className="text-xs text-muted-foreground">
                        {session.project ?? "No project"} ·{" "}
                        {formatDateTime(session.lastActivityAt ?? session.startedAt)}
                      </p>
                      <p className="text-xs text-muted-foreground">
                        {formatCompact(session.totalTokens)} lifetime tokens · {formatInteger(session.toolCallCount)}{" "}
                        tool calls
                      </p>
                    </div>
                  ))
                )}
                <Button asChild variant="outline">
                  <Link to="/sessions" search={DEFAULT_SESSIONS_SEARCH}>
                    All sessions <ArrowRight className="size-4" aria-hidden />
                  </Link>
                </Button>
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <ShieldCheck className="size-4" aria-hidden /> Harness insights
                </CardTitle>
                <CardDescription>Evidence before conclusions.</CardDescription>
              </CardHeader>
              <CardContent className="space-y-3 text-sm">
                <p>
                  {formatInteger(query.data.sessionsWithVersionEvidence)} of {formatInteger(query.data.totalSessions)}{" "}
                  sessions have harness-version evidence.
                </p>
                <p>
                  {formatInteger(query.data.hookFailures)} recorded hook failures across{" "}
                  {formatInteger(query.data.hooks)} executions.
                </p>
                <p className="text-muted-foreground">
                  Failures and guard blocks need context; neither alone proves a harness regression.
                </p>
                <Button asChild variant="outline">
                  <Link to="/harness">
                    Explore harness health <ArrowRight className="size-4" aria-hidden />
                  </Link>
                </Button>
              </CardContent>
            </Card>
          </div>
          <p className="text-xs text-muted-foreground">
            Snapshot {formatDateTime(query.data.generatedAt)} · Totals cover all indexed history, not just recent
            sessions. Recorded tokens are not an account balance.
          </p>
        </>
      )}
      <div className="grid items-start gap-4 lg:grid-cols-2">
        <HarnessInstallations />
        <Card>
          <CardHeader>
            <CardTitle>Claude allowance</CardTitle>
            <CardDescription>Remaining subscription allowance and API credits</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2 text-sm">
            <p className="font-medium">Allowance unavailable</p>
            <p className="text-muted-foreground">
              Local session logs do not establish your account balance or reset time. No account connection is
              configured; recorded token usage is shown separately.
            </p>
          </CardContent>
        </Card>
      </div>
    </div>
  );
};

export default DashboardPage;
