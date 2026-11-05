import { useNavigate, useSearch } from "@tanstack/react-router";
import { Inbox } from "lucide-react";
import { useCallback } from "react";
import { QueryErrorAlert } from "@/common/QueryErrorAlert";
import { useSessionsList } from "@/features/sessions/hooks/useSessions";
import { PaginationBar } from "@/features/sessions/components/PaginationBar";
import { SessionCardList } from "@/features/sessions/components/SessionCardList";
import { SessionsSkeleton } from "@/features/sessions/components/SessionsSkeleton";
import { SessionsTable } from "@/features/sessions/components/SessionsTable";
import { SessionsToolbar } from "@/features/sessions/components/SessionsToolbar";
import { SummaryStrip } from "@/features/sessions/components/SummaryStrip";
import type { SessionsSearch } from "@/features/sessions/searchParams";
import type { SortField } from "@/features/sessions/schemas";

const SessionsPage = () => {
  const search = useSearch({ from: "/" });
  const navigate = useNavigate({ from: "/" });

  const query = useSessionsList({
    page: search.page,
    pageSize: search.pageSize,
    source: search.source === "all" ? undefined : search.source,
    search: search.search,
    sort: search.sort,
    direction: search.direction,
  });

  const update = useCallback(
    (patch: Partial<SessionsSearch>) => {
      void navigate({ search: (prev) => ({ ...prev, ...patch }) });
    },
    [navigate],
  );

  const handleSort = useCallback(
    (field: SortField) => {
      update({
        sort: field,
        direction: field === search.sort && search.direction === "desc" ? "asc" : "desc",
        page: 1,
      });
    },
    [update, search.sort, search.direction],
  );

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Sessions</h1>
        <p className="text-sm text-muted-foreground">Your Copilot and Claude CLI sessions, most recently active first.</p>
      </div>

      <SessionsToolbar
        search={search.search}
        source={search.source}
        pageSize={search.pageSize}
        onSearchChange={(value) => update({ search: value, page: 1 })}
        onSourceChange={(value) => update({ source: value, page: 1 })}
        onPageSizeChange={(value) => update({ pageSize: value, page: 1 })}
      />

      {query.isPending ? (
        <SessionsSkeleton />
      ) : query.isError ? (
        <QueryErrorAlert title="Could not load sessions" error={query.error} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <div className="flex flex-col items-center gap-2 rounded-lg border border-dashed py-16 text-center">
          <Inbox className="size-8 text-muted-foreground" aria-hidden />
          <p className="font-medium">No sessions found</p>
          <p className="text-sm text-muted-foreground">Try a different search or source filter.</p>
        </div>
      ) : (
        <div className="space-y-4" aria-busy={query.isPlaceholderData}>
          <SummaryStrip sessions={query.data.items} totalCount={query.data.totalCount} />
          <div className="hidden md:block">
            <SessionsTable
              sessions={query.data.items}
              sort={search.sort}
              direction={search.direction}
              onSort={handleSort}
            />
          </div>
          <div className="md:hidden">
            <SessionCardList sessions={query.data.items} />
          </div>
          <PaginationBar
            page={query.data.page}
            pageSize={query.data.pageSize}
            totalCount={query.data.totalCount}
            totalPages={query.data.totalPages}
            onPageChange={(page) => update({ page })}
          />
        </div>
      )}
    </div>
  );
};

export default SessionsPage;
