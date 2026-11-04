import type { ReactNode } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

interface ChartCardProps {
  title: string;
  description?: string;
  /** Plain-language summary read by assistive tech instead of the (non-navigable) SVG. */
  summary: string;
  empty: boolean;
  emptyMessage?: string;
  children: ReactNode;
  className?: string;
}

export const ChartCard = ({
  title,
  description,
  summary,
  empty,
  emptyMessage = "No data recorded for this session.",
  children,
  className,
}: ChartCardProps) => (
  <Card className={className}>
    <CardHeader>
      <CardTitle className="text-base">{title}</CardTitle>
      {description && <CardDescription>{description}</CardDescription>}
    </CardHeader>
    <CardContent>
      {empty ? (
        <p className="py-10 text-center text-sm text-muted-foreground">{emptyMessage}</p>
      ) : (
        <div role="img" aria-label={`${title}. ${summary}`}>
          {children}
        </div>
      )}
    </CardContent>
  </Card>
);
