import { useState } from "react";
import { isActiveRun, type ReportRun } from "../schemas";
import { useReportRuns } from "./useReports";

/**
 * Derives which run is shown. The user's pick wins; otherwise the latest succeeded version, so a report is
 * re-openable on page load and a finished run appears by itself.
 */
export const useReportPanel = (sessionId: string) => {
  const runsQuery = useReportRuns(sessionId);
  const [pickedId, setPickedId] = useState<string | null>(null);

  const runs: ReportRun[] = runsQuery.data ?? [];
  const activeRun = runs.find(isActiveRun) ?? null;
  const selectedRun =
    runs.find((run) => run.id === pickedId) ?? runs.find((run) => run.status === "succeeded") ?? runs[0] ?? null;
  const hasReport = runs.some((run) => run.status === "succeeded");

  return {
    runsQuery,
    runs,
    activeRun,
    selectedRun,
    hasReport,
    pickRun: setPickedId,
    clearPick: () => setPickedId(null),
  };
};
