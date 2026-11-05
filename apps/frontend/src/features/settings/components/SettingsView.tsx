import { QueryErrorAlert } from "@/common/QueryErrorAlert";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { useSetTerminal, useSettings } from "../hooks/useSettings";

export const SettingsView = () => {
  const query = useSettings();
  const setTerminal = useSetTerminal();

  if (query.isPending) return <Skeleton className="h-40 w-full" role="status" aria-label="Loading settings" />;
  if (query.isError) {
    return <QueryErrorAlert title="Could not load the settings" error={query.error} onRetry={() => void query.refetch()} />;
  }

  const { terminal, terminals } = query.data;
  return (
    <Card>
      <CardHeader>
        <CardTitle>Terminal</CardTitle>
        <CardDescription>
          Where "Continue in CLI" opens a session. Options that are not installed on this machine are disabled.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Select value={terminal} onValueChange={(value) => setTerminal.mutate(value)} disabled={setTerminal.isPending}>
          <SelectTrigger className="w-full max-w-sm" aria-label="Terminal">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {terminals.map((option) => (
              <SelectItem key={option.id} value={option.id} disabled={!option.available}>
                {option.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </CardContent>
    </Card>
  );
};
