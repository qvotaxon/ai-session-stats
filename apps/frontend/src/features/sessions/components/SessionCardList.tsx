import { Card, CardContent } from "@/components/ui/card";
import { ReportStatusBadge } from "@/features/reports/components/ReportStatusBadge";
import { formatInteger } from "@/lib/format";
import type { SessionSummary } from "../schemas";
import { Duration, ModelsCell, SessionTitleLink, StartedAt, Tokens } from "./SessionCells";
import { SourceBadge } from "./SourceBadge";

const Stat = ({ label, children }: { label: string; children: React.ReactNode }) => (
  <div>
    <dt className="text-xs text-muted-foreground">{label}</dt>
    <dd className="text-sm font-medium">{children}</dd>
  </div>
);

export const SessionCardList = ({ sessions }: { sessions: SessionSummary[] }) => (
  <ul className="space-y-3" aria-label="Sessions">
    {sessions.map((session) => (
      <li key={session.id}>
        <Card className="py-4">
          <CardContent className="space-y-3 px-4">
            <div className="flex items-start justify-between gap-2">
              <SessionTitleLink session={session} />
              <SourceBadge source={session.source} />
            </div>
            <div className="text-xs text-muted-foreground">
              {session.project ?? "No project"} · <StartedAt iso={session.lastActivityAt ?? session.startedAt} />
            </div>
            <dl className="grid grid-cols-3 gap-3">
              <Stat label="Duration">
                <Duration seconds={session.durationSeconds} />
              </Stat>
              <Stat label="Tokens">
                <Tokens value={session.totalTokens} />
              </Stat>
              <Stat label="Tools">{formatInteger(session.toolCallCount)}</Stat>
              <Stat label="Hooks">{formatInteger(session.hookCount)}</Stat>
              <Stat label="Guards">{formatInteger(session.guardCount)}</Stat>
              <Stat label="Subagents">{formatInteger(session.subagentCount)}</Stat>
            </dl>
            <div className="flex items-center justify-between gap-2 text-xs">
              <ModelsCell models={session.models} />
              <ReportStatusBadge status={session.latestReportStatus} />
            </div>
          </CardContent>
        </Card>
      </li>
    ))}
  </ul>
);
