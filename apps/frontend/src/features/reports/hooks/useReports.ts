import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { showToast } from "@/common/toast";
import { toastApiError } from "@/common/toastApiError";
import { isApiError } from "@/infrastructure/api";
import { saveBlob } from "@/lib/download";
import { sessionKeys } from "@/features/sessions/hooks/useSessions";
import { reportsApi } from "../api/reportsApi";
import { isActiveRun, type ReportRun } from "../schemas";

const POLL_INTERVAL_MS = 2000;

export const reportKeys = {
  runs: (sessionId: string) => ["reports", sessionId, "runs"] as const,
  content: (sessionId: string, runId: string) => ["reports", sessionId, "content", runId] as const,
};

/** Polls only while a run is queued or running. */
export const useReportRuns = (sessionId: string) =>
  useQuery({
    queryKey: reportKeys.runs(sessionId),
    queryFn: ({ signal }) => reportsApi.list(sessionId, signal),
    refetchInterval: (query) => (query.state.data?.some(isActiveRun) ? POLL_INTERVAL_MS : false),
  });

export const useReportContent = (sessionId: string, runId: string | null) =>
  useQuery({
    queryKey: reportKeys.content(sessionId, runId ?? ""),
    queryFn: ({ signal }) => reportsApi.content(sessionId, runId as string, signal),
    enabled: runId !== null,
    staleTime: Infinity,
  });

const useRefreshAfterRunChange = (sessionId: string) => {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: reportKeys.runs(sessionId) }),
      queryClient.invalidateQueries({ queryKey: sessionKeys.all }),
    ]);
};

export const useGenerateReport = (sessionId: string) => {
  const refresh = useRefreshAfterRunChange(sessionId);
  return useMutation({
    mutationFn: () => reportsApi.create(sessionId),
    onSuccess: () => refresh(),
    onError: (error) => {
      if (isApiError(error, 409)) {
        showToast("warn", "A report is already running", "Wait for it to finish or cancel it first.");
        void refresh();
      } else {
        toastApiError(error, "Could not start the report");
      }
    },
  });
};

export const useCancelReport = (sessionId: string) => {
  const refresh = useRefreshAfterRunChange(sessionId);
  return useMutation({
    mutationFn: (run: ReportRun) => reportsApi.cancel(sessionId, run.id),
    onSuccess: () => refresh(),
    onError: (error) => {
      if (isApiError(error, 409)) {
        showToast("warn", "Report is no longer running");
        void refresh();
      } else {
        toastApiError(error, "Could not cancel the report");
      }
    },
  });
};

export const useDownloadReport = (sessionId: string) =>
  useMutation({
    mutationFn: async (run: ReportRun) => {
      const blob = await reportsApi.download(sessionId, run.id);
      saveBlob(blob, `session-${sessionId}-v${run.version}.md`);
    },
    onError: (error) => toastApiError(error, "Download failed"),
  });
