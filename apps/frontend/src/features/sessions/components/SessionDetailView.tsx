import { ArrowLeft, Info } from "lucide-react";
import { Link } from "@tanstack/react-router";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Separator } from "@/components/ui/separator";
import { ReportPanel } from "@/features/reports/components/ReportPanel";
import { ReportStatusBadge } from "@/features/reports/components/ReportStatusBadge";
import { formatDateTime, formatDuration } from "@/lib/format";
import { DEFAULT_SESSIONS_SEARCH } from "../searchParams";
import type { SessionDetail } from "../schemas";
import { CountListCard } from "./detail/CountListCard";
import { GuardsPanel } from "./detail/GuardsPanel";
import { HooksChart } from "./detail/HooksChart";
import { KpiGrid } from "./detail/KpiGrid";
import { TimelineChart } from "./detail/TimelineChart";
import { TokenBreakdownChart } from "./detail/TokenBreakdownChart";
import { TokensByModelChart } from "./detail/TokensByModelChart";
import { ToolCallsChart, ToolDurationChart } from "./detail/ToolCharts";
import { SourceBadge } from "./SourceBadge";

export const SessionDetailView = ({ session }: { session: SessionDetail }) => (
  <div className="space-y-6">
    <div>
      <Link
        to="/"
        search={DEFAULT_SESSIONS_SEARCH}
        className="mb-3 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
      >
        <ArrowLeft className="size-4" aria-hidden /> All sessions
      </Link>
      <div className="flex flex-wrap items-center gap-2">
        <SourceBadge source={session.source} />
        {session.latestReportStatus && <ReportStatusBadge status={session.latestReportStatus} />}
      </div>
      <h1 className="mt-2 text-2xl font-semibold tracking-tight break-words">{session.title || session.id}</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        {session.project ?? "No project"} · started {formatDateTime(session.startedAt)}
        {session.endedAt ? ` · ended ${formatDateTime(session.endedAt)}` : ""} ·{" "}
        {formatDuration(session.durationSeconds)}
      </p>
      <p className="mt-1 text-xs break-all text-muted-foreground">{session.id}</p>
    </div>

    {session.notes.length > 0 && (
      <Alert>
        <Info className="size-4" aria-hidden />
        <AlertTitle>Not available for this CLI</AlertTitle>
        <AlertDescription>
          <ul className="list-disc space-y-1 pl-4">
            {session.notes.map((note) => (
              <li key={note}>{note}</li>
            ))}
          </ul>
        </AlertDescription>
      </Alert>
    )}

    <KpiGrid session={session} />

    <ReportPanel sessionId={session.id} />

    <Separator />

    <div className="grid gap-4 lg:grid-cols-2">
      <TokenBreakdownChart tokens={session.tokens} />
      <TokensByModelChart models={session.tokensByModel} />
      <ToolCallsChart tools={session.toolCalls.byTool} />
      <ToolDurationChart tools={session.toolCalls.byTool} />
      <HooksChart hooks={session.hooks} />
      <GuardsPanel guards={session.guards} />
      <CountListCard
        title="Subagents"
        description="Subagent runs by type"
        items={session.subagents.byType}
        emptyMessage="No subagents were used."
      />
      <CountListCard
        title="Skills"
        description="Skills invoked"
        items={session.skills}
        emptyMessage="No skills were used."
      />
      <TimelineChart timeline={session.timeline} />
    </div>
  </div>
);
