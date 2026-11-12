import { Download, FileText, Loader2, RefreshCw, Square } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime, formatDuration } from "@/lib/format";
import { useCancelReport, useDownloadReport, useGenerateReport, useReportContent } from "../hooks/useReports";
import { useReportPanel } from "../hooks/useReportPanel";
import type { ReportRun } from "../schemas";
import { MarkdownView } from "@/common/MarkdownView";
import { ReportStatusBadge } from "./ReportStatusBadge";

const runLabel = (run: ReportRun): string => {
  const duration = run.durationMs === null ? "" : ` · ${formatDuration(run.durationMs / 1000)}`;
  return `v${run.version} · ${run.status} · ${formatDateTime(run.startedAt)}${duration}`;
};

const ReportBody = ({ sessionId, run }: { sessionId: string; run: ReportRun | null }) => {
  const content = useReportContent(sessionId, run?.status === "succeeded" ? run.id : null);

  if (run === null) {
    return (
      <p className="py-8 text-center text-sm text-muted-foreground">
        No report yet. Generate one to get a markdown summary of this session.
      </p>
    );
  }
  if (run.status === "queued" || run.status === "running") {
    return (
      <p className="flex items-center justify-center gap-2 py-8 text-sm text-muted-foreground" role="status">
        <Loader2 className="size-4 animate-spin" aria-hidden />
        {run.status === "queued" ? "Report queued…" : "Generating report… this can take a few minutes."}
      </p>
    );
  }
  if (run.status === "failed") {
    return (
      <Alert variant="destructive">
        <AlertTitle>Report v{run.version} failed</AlertTitle>
        <AlertDescription>{run.error ?? "The report run failed without an error message."}</AlertDescription>
      </Alert>
    );
  }
  if (run.status === "cancelled") {
    return <p className="py-8 text-center text-sm text-muted-foreground">Report v{run.version} was cancelled.</p>;
  }
  if (content.isPending) return <Skeleton className="h-64 w-full" />;
  if (content.isError) {
    return (
      <Alert variant="destructive">
        <AlertTitle>Could not load the report</AlertTitle>
        <AlertDescription>{content.error.message}</AlertDescription>
      </Alert>
    );
  }
  return (
    <div
      className="max-h-[32rem] overflow-auto rounded-md border"
      data-testid="report-content"
      tabIndex={0}
      aria-label="Report content"
    >
      <MarkdownView className="p-4 text-sm">{content.data}</MarkdownView>
    </div>
  );
};

export const ReportPanel = ({ sessionId }: { sessionId: string }) => {
  const { runsQuery, runs, activeRun, selectedRun, hasReport, pickRun, clearPick } = useReportPanel(sessionId);
  const generate = useGenerateReport(sessionId);
  const cancel = useCancelReport(sessionId);
  const download = useDownloadReport(sessionId);

  const busy = generate.isPending || activeRun !== null;
  const generateLabel = hasReport ? "Re-run analysis" : "Generate session report";

  return (
    <Card>
      <CardHeader className="gap-3">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="space-y-1.5">
            <CardTitle className="flex items-center gap-2">
              <FileText className="size-5" aria-hidden /> Session report
            </CardTitle>
            <CardDescription>
              An AI-written markdown summary of this session. Each run creates a new version.
            </CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {activeRun?.status === "running" && (
              <Button variant="outline" onClick={() => cancel.mutate(activeRun)} disabled={cancel.isPending}>
                <Square className="size-4" aria-hidden /> Cancel
              </Button>
            )}
            <Button
              onClick={() =>
                generate.mutate(undefined, {
                  onSuccess: () => clearPick(),
                })
              }
              disabled={busy}
            >
              {busy ? (
                <Loader2 className="size-4 animate-spin" aria-hidden />
              ) : hasReport ? (
                <RefreshCw className="size-4" aria-hidden />
              ) : (
                <FileText className="size-4" aria-hidden />
              )}
              {busy ? "Generating…" : generateLabel}
            </Button>
          </div>
        </div>

        {runs.length > 0 && (
          <div className="flex flex-wrap items-center gap-2">
            <Select value={selectedRun?.id ?? ""} onValueChange={pickRun}>
              <SelectTrigger className="w-full sm:w-[26rem]" aria-label="Report version">
                <SelectValue placeholder="Select a version" />
              </SelectTrigger>
              <SelectContent>
                {runs.map((run) => (
                  <SelectItem key={run.id} value={run.id}>
                    {runLabel(run)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {selectedRun && <ReportStatusBadge status={selectedRun.status} />}
            {selectedRun?.status === "succeeded" && (
              <Button
                variant="outline"
                onClick={() => download.mutate(selectedRun)}
                disabled={download.isPending}
                className="sm:ml-auto"
              >
                <Download className="size-4" aria-hidden /> Download .md
              </Button>
            )}
          </div>
        )}
      </CardHeader>
      <CardContent>
        {runsQuery.isPending ? (
          <Skeleton className="h-24 w-full" />
        ) : runsQuery.isError ? (
          <Alert variant="destructive">
            <AlertTitle>Could not load reports</AlertTitle>
            <AlertDescription>{runsQuery.error.message}</AlertDescription>
          </Alert>
        ) : (
          <ReportBody sessionId={sessionId} run={selectedRun} />
        )}
      </CardContent>
    </Card>
  );
};
