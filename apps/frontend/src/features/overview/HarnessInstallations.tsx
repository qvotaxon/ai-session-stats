import { useEffect, useRef } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { QueryErrorAlert } from "@/common/QueryErrorAlert";
import { showToast } from "@/common/toast";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { SourceBadge } from "@/features/sessions/components/SourceBadge";
import { formatDateTime } from "@/lib/format";
import { useCheckHarnessUpdates, useHarnessInstallations, useHarnessUpdates, useUpdateHarness } from "./api";

const harnessNotifications = new Set<string>();

export const HarnessInstallations = () => {
  const query = useHarnessInstallations();
  const updates = useHarnessUpdates();
  const update = useUpdateHarness();
  const check = useCheckHarnessUpdates();
  const client = useQueryClient();
  const seen = useRef(harnessNotifications);
  useEffect(() => {
    for (const state of updates.data ?? []) {
      if (state.completedAt && !seen.current.has(`${state.source}:${state.completedAt}`)) {
        seen.current.add(`${state.source}:${state.completedAt}`);
        showToast(
          state.completionStatus === "error" ? "error" : "success",
          `${state.source} MSDC harness ${state.completionStatus === "error" ? "update failed" : "updated"}`,
          state.completionError ?? "Restart the CLI to use the new version.",
        );
        void client.invalidateQueries({ queryKey: ["harness", "installations"] });
      } else if (state.available && !seen.current.has(`${state.source}:${state.latestVersion}`)) {
        seen.current.add(`${state.source}:${state.latestVersion}`);
        showToast("info", `${state.source} MSDC harness update available`, `Version ${state.latestVersion}`);
      }
    }
  }, [updates.data, client]);
  return (
    <Card>
      <CardHeader>
        <CardTitle>Installed MSDC harness</CardTitle>
        <CardDescription>
          Installed now does not mean loaded by a past or open session. Restart the CLI after updating.
        </CardDescription>
        <Button
          variant="outline"
          size="sm"
          disabled={check.isPending || updates.data?.some((state) => state.status === "checking" || state.status === "pending")}
          onClick={() => check.mutate()}
        >
          Check for plugin updates
        </Button>
      </CardHeader>
      <CardContent>
        {updates.isError && (
          <QueryErrorAlert title="Could not check plugin updates" error={updates.error} onRetry={() => void updates.refetch()} />
        )}
        {query.isPending ? (
          <p role="status" className="text-sm text-muted-foreground">
            Checking local CLIs...
          </p>
        ) : query.isError ? (
          <QueryErrorAlert
            title="Could not check installed plugins"
            error={query.error}
            onRetry={() => void query.refetch()}
          />
        ) : (
          <div className="grid gap-4 sm:grid-cols-2">
            {query.data.map((inventory) => (
              <div key={inventory.source} className="space-y-2 rounded-md border p-3">
                <SourceBadge source={inventory.source} />
                {updates.data?.filter((state) => state.source === inventory.source).map((state) => (
                  <div key={state.source} className="space-y-2 text-sm">
                    {state.status === "checking" || state.status === "pending" ? <p role="status">Checking for updates...</p> : null}
                    {state.status === "updating" ? <p role="status">Updating in the background...</p> : null}
                    {state.error ? <p role="alert">{state.error}</p> : null}
                    {state.status === "succeeded" ? <p role="status">Updated to {state.latestVersion}. Restart the CLI.</p> : null}
                    {state.available && state.status !== "updating" ? (
                      <Button size="sm" disabled={update.isPending} onClick={() => update.mutate(state.source)}>
                        Update {state.source} to {state.latestVersion}
                      </Button>
                    ) : null}
                  </div>
                ))}
                {inventory.status === "installed" ? (
                  inventory.installations.map((plugin, index) => (
                    <p key={`${plugin.version}-${plugin.scope}-${index}`} className="text-sm font-medium">
                      {plugin.version}
                      {plugin.scope ? ` (${plugin.scope})` : ""}
                      {plugin.enabled === false
                        ? " · Disabled"
                        : plugin.enabled === null
                          ? " · Enablement unknown"
                          : ""}
                    </p>
                  ))
                ) : (
                  <p className="text-sm" role={inventory.status === "error" ? "alert" : undefined}>
                    {inventory.status === "notInstalled" ? "Not installed in this inventory scope." : inventory.error}
                  </p>
                )}
                <p className="text-xs text-muted-foreground">Checked {formatDateTime(inventory.checkedAt)}</p>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
};
