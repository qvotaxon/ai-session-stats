import { QueryErrorAlert } from "@/common/QueryErrorAlert";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { SourceBadge } from "@/features/sessions/components/SourceBadge";
import { formatDateTime } from "@/lib/format";
import { useHarnessInstallations } from "./api";

export const HarnessInstallations = () => {
  const query = useHarnessInstallations();
  return (
    <Card>
      <CardHeader>
        <CardTitle>Installed MSDC harness</CardTitle>
        <CardDescription>
          Read-only CLI inventory. Installed now does not mean loaded by a past or open session.
        </CardDescription>
      </CardHeader>
      <CardContent>
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
