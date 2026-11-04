import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { SessionSource } from "../schemas";

const LABEL: Record<SessionSource, string> = { copilot: "Copilot", claude: "Claude" };
const STYLE: Record<SessionSource, string> = {
  copilot: "bg-chart-1/15 text-chart-1",
  claude: "bg-chart-3/20 text-chart-3",
};

export const SourceBadge = ({ source }: { source: SessionSource }) => (
  <Badge variant="outline" className={cn("border-transparent font-medium", STYLE[source])}>
    {LABEL[source]}
  </Badge>
);
