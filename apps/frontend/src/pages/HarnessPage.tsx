import { Link } from "@tanstack/react-router";
import { QueryErrorAlert } from "@/common/QueryErrorAlert";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { HarnessInstallations } from "@/features/overview/HarnessInstallations";
import { useOverview } from "@/features/overview/api";
import { SourceBadge } from "@/features/sessions/components/SourceBadge";
import { formatInteger } from "@/lib/format";

const rate = (failed: number, total: number) =>
  total === 0
    ? "Not attributed"
    : `${formatInteger(failed)} / ${formatInteger(total)} (${((100 * failed) / total).toFixed(1)}%)`;

const HarnessPage = () => {
  const query = useOverview();
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">MSDC harness</h1>
        <p className="text-sm text-muted-foreground">
          Compare observed versions and inspect the evidence behind potential regressions.
        </p>
      </div>
      <HarnessInstallations />
      <Card>
        <CardHeader>
          <CardTitle>Version evidence and session health</CardTitle>
          <CardDescription>
            All indexed history. Rows are separated by provider, harness version, and recorded CLI version.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {query.isPending ? (
            <p role="status">Loading harness evidence...</p>
          ) : query.isError ? (
            <QueryErrorAlert
              title="Could not load harness evidence"
              error={query.error}
              onRetry={() => void query.refetch()}
            />
          ) : (
            <>
              <p className="text-sm">
                Session evidence: {formatInteger(query.data.sessionsWithVersionEvidence)} /{" "}
                {formatInteger(query.data.totalSessions)}. Health-event attribution:{" "}
                {formatInteger(query.data.attributedHealthEvents)} / {formatInteger(query.data.totalHealthEvents)}.{" "}
                {formatInteger(query.data.totalHealthEvents - query.data.attributedHealthEvents)} tool/hook events
                remain unattributed.
              </p>
              {query.data.harnessVersions.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  No harness-version evidence found. Unknown does not mean the harness was absent.
                </p>
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Provider / CLI</TableHead>
                      <TableHead>Harness</TableHead>
                      <TableHead>Sessions</TableHead>
                      <TableHead>Evidence records</TableHead>
                      <TableHead>Tool failures</TableHead>
                      <TableHead>Hook failures</TableHead>
                      <TableHead>Supporting sessions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {query.data.harnessVersions.map((version) => (
                      <TableRow key={`${version.source}-${version.version}-${version.cliVersion}`}>
                        <TableCell>
                          <SourceBadge source={version.source} />
                          <div className="mt-1 text-xs text-muted-foreground">
                            {version.cliVersion ?? "CLI version unknown"}
                          </div>
                        </TableCell>
                        <TableCell className="font-medium">{version.version}</TableCell>
                        <TableCell>{formatInteger(version.sessions)}</TableCell>
                        <TableCell>
                          {version.activations > 0
                            ? `${version.activations} indirect activation records`
                            : `${version.directInvocations} direct invocation records`}
                        </TableCell>
                        <TableCell>{rate(version.toolFailures, version.toolCalls)}</TableCell>
                        <TableCell>{rate(version.hookFailures, version.hooks)}</TableCell>
                        <TableCell>
                          <details>
                            <summary className="cursor-pointer">View sessions</summary>
                            <div className="space-y-1 py-2">
                              {version.sessionIds.map((id) => (
                                <Link
                                  key={id}
                                  to="/sessions/$sessionId"
                                  params={{ sessionId: id }}
                                  className="block text-xs underline"
                                >
                                  {id}
                                </Link>
                              ))}
                            </div>
                          </details>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </>
          )}
          <div className="space-y-2 text-sm text-muted-foreground">
            <p>
              Claude attribution is inferred from version-bearing SessionStart hook paths until the next recorded
              start/resume. A missing or ambiguous version resets attribution to unknown. Subagent activity is not
              assigned to a main-session activation.
            </p>
            <p>
              Copilot versions identify individual skill invocations, not a loaded-version window. General tool and hook
              health stays unattributed for Copilot.
            </p>
            <p>
              These are session-health comparisons, not measured plugin-component effectiveness. Projects, models, and
              task difficulty can differ. No causal regression verdicts or version-specific token totals are inferred.
            </p>
          </div>
        </CardContent>
      </Card>
    </div>
  );
};

export default HarnessPage;
