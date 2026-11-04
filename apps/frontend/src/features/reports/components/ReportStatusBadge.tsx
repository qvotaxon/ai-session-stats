import { AlertTriangle, CheckCircle2, CircleSlash, Clock, Loader2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { ReportStatus } from "@/features/sessions/schemas";
import { cn } from "@/lib/utils";

const STATUS: Record<ReportStatus, { label: string; className: string; icon: typeof Clock }> = {
  queued: { label: "Queued", className: "bg-muted text-muted-foreground", icon: Clock },
  running: { label: "Running", className: "bg-chart-1/15 text-chart-1", icon: Loader2 },
  succeeded: { label: "Report ready", className: "bg-chart-5/15 text-chart-5", icon: CheckCircle2 },
  failed: { label: "Failed", className: "bg-destructive/15 text-destructive", icon: AlertTriangle },
  cancelled: { label: "Cancelled", className: "bg-muted text-muted-foreground", icon: CircleSlash },
};

export const ReportStatusBadge = ({ status }: { status: ReportStatus | null }) => {
  if (status === null) return <span className="text-muted-foreground">–</span>;
  const { label, className, icon: Icon } = STATUS[status];
  return (
    <Badge variant="outline" className={cn("gap-1 border-transparent font-medium", className)}>
      <Icon className={cn("size-3", status === "running" && "animate-spin")} aria-hidden />
      {label}
    </Badge>
  );
};
