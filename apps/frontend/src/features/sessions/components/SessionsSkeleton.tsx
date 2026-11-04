import { Skeleton } from "@/components/ui/skeleton";

const ROWS = ["a", "b", "c", "d", "e", "f"] as const;

export const SessionsSkeleton = () => (
  <div className="space-y-3" role="status" aria-label="Loading sessions">
    {ROWS.map((key) => (
      <Skeleton key={key} className="h-14 w-full" />
    ))}
  </div>
);
