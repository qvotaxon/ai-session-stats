import { useParams } from "@tanstack/react-router";
import { QueryErrorAlert } from "@/common/QueryErrorAlert";
import { NotFound } from "@/components/layout/NotFound";
import { Skeleton } from "@/components/ui/skeleton";
import { isApiError } from "@/infrastructure/api";
import { SessionDetailView } from "@/features/sessions/components/SessionDetailView";
import { useSessionDetail } from "@/features/sessions/hooks/useSessions";

const SessionDetailPage = () => {
  const { sessionId } = useParams({ from: "/sessions/$sessionId" });
  const query = useSessionDetail(sessionId);

  if (query.isPending) {
    return (
      <div className="space-y-4" role="status" aria-label="Loading session">
        <Skeleton className="h-10 w-2/3" />
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }
  if (query.isError) {
    return isApiError(query.error, 404) ? (
      <NotFound title="Session not found" message="This session does not exist, or it is no longer available." />
    ) : (
      <QueryErrorAlert title="Could not load the session" error={query.error} onRetry={() => void query.refetch()} />
    );
  }
  return <SessionDetailView session={query.data} />;
};

export default SessionDetailPage;
