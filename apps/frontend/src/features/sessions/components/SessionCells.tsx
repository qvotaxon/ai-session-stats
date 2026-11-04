import { Link } from "@tanstack/react-router";
import { formatCompact, formatDateTime, formatDuration, formatInteger, formatRelative } from "@/lib/format";
import type { SessionSummary } from "../schemas";

export const SessionTitleLink = ({ session }: { session: SessionSummary }) => (
  <Link
    to="/sessions/$sessionId"
    params={{ sessionId: session.id }}
    className="line-clamp-2 font-medium break-words underline-offset-4 hover:underline focus-visible:rounded-sm focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
  >
    {session.title || session.id}
  </Link>
);

export const StartedAt = ({ iso }: { iso: string }) => (
  <time dateTime={iso} title={formatDateTime(iso)} className="whitespace-nowrap">
    {formatRelative(iso)}
    <span className="block text-xs text-muted-foreground">{formatDateTime(iso)}</span>
  </time>
);

export const ModelsCell = ({ models }: { models: string[] }) => {
  if (models.length === 0) return <span className="text-muted-foreground">–</span>;
  const [first, ...rest] = models;
  return (
    <span title={models.join(", ")} className="block max-w-[9rem] truncate">
      {first}
      {rest.length > 0 && <span className="text-muted-foreground"> +{rest.length}</span>}
    </span>
  );
};

export const Tokens = ({ value }: { value: number }) => (
  <span title={formatInteger(value)}>{formatCompact(value)}</span>
);

export const Duration = ({ seconds }: { seconds: number }) => <span>{formatDuration(seconds)}</span>;
