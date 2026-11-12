import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Copy, RefreshCw } from "lucide-react";
import { QueryErrorAlert } from "@/common/QueryErrorAlert";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { logsApi } from "@/features/application-log/api/logsApi";

const ApplicationLogPage = () => {
  const [copyStatus, setCopyStatus] = useState("");
  const query = useQuery({
    queryKey: ["application-log"],
    queryFn: ({ signal }) => logsApi.get(signal),
    staleTime: 0,
    gcTime: 0,
  });

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(query.data?.content ?? "");
      setCopyStatus("Log copied.");
    } catch {
      setCopyStatus("Could not copy the log. Select and copy the text below instead.");
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">Application Log</h1>
        <div className="flex gap-2">
          <Button variant="outline" disabled={!query.data?.content} onClick={() => void copy()}>
            <Copy className="size-4" aria-hidden /> Copy log
          </Button>
          <Button
            variant="outline"
            disabled={query.isFetching}
            onClick={() => {
              setCopyStatus("");
              void query.refetch();
            }}
          >
            <RefreshCw className="size-4" aria-hidden /> Refresh
          </Button>
        </div>
      </div>
      <p className="text-sm text-muted-foreground">
        Application-wide backend diagnostics, including API, session indexing and report failures, from the latest log
        file. Logs may contain local paths and session identifiers. Review them before sharing.
      </p>
      {copyStatus && (
        <p role="status" className="text-sm">
          {copyStatus}
        </p>
      )}
      {query.isPending && <Skeleton className="h-64 w-full" role="status" aria-label="Loading application log" />}
      {query.isError && (
        <QueryErrorAlert
          title="Could not load the application log"
          error={query.error}
          onRetry={() => void query.refetch()}
        />
      )}
      {query.data && (
        <>
          {query.data.fileName && <p className="text-sm text-muted-foreground">{query.data.fileName}</p>}
          {query.data.truncated && (
            <p className="text-sm text-muted-foreground">
              Showing only the last 256 KiB of the latest log file; the partial first line is omitted.
            </p>
          )}
          {query.data.content ? (
            <pre
              aria-label="Application log contents"
              tabIndex={0}
              className="max-h-[65vh] overflow-auto rounded-md border bg-muted p-4 text-xs whitespace-pre-wrap break-words"
            >
              {query.data.content}
            </pre>
          ) : (
            <p className="rounded-md border p-6 text-muted-foreground">No application log entries available.</p>
          )}
        </>
      )}
    </div>
  );
};

export default ApplicationLogPage;
