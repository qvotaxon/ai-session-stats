import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { formatInteger } from "@/lib/format";

interface PaginationBarProps {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  onPageChange: (page: number) => void;
}

const getPageItems = (page: number, total: number): (number | "ellipsis-start" | "ellipsis-end")[] => {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const pages = new Set([1, total, page - 1, page, page + 1]);
  const sorted = [...pages].filter((p) => p >= 1 && p <= total).sort((a, b) => a - b);
  const items: (number | "ellipsis-start" | "ellipsis-end")[] = [];
  sorted.forEach((p, i) => {
    const prev = sorted[i - 1];
    if (prev !== undefined && p - prev > 1) items.push(p - prev === 2 ? prev + 1 : i < 2 ? "ellipsis-start" : "ellipsis-end");
    items.push(p);
  });
  return items;
};

export const PaginationBar = ({ page, pageSize, totalCount, totalPages, onPageChange }: PaginationBarProps) => {
  const first = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, totalCount);
  return (
    <nav aria-label="Pagination" className="flex flex-wrap items-center justify-between gap-3">
      <p className="text-sm text-muted-foreground" aria-live="polite">
        {formatInteger(first)}–{formatInteger(last)} of {formatInteger(totalCount)} sessions
      </p>
      <div className="flex flex-wrap items-center gap-1">
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => onPageChange(1)}
          disabled={page <= 1}
          aria-label="First page"
        >
          <ChevronsLeft className="size-4" aria-hidden />
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
          <ChevronLeft className="size-4" aria-hidden /> Previous
        </Button>
        {getPageItems(page, Math.max(totalPages, 1)).map((item) =>
          typeof item === "number" ? (
            <Button
              key={item}
              type="button"
              variant={item === page ? "default" : "outline"}
              size="sm"
              onClick={() => onPageChange(item)}
              aria-current={item === page ? "page" : undefined}
              aria-label={`Page ${item}`}
            >
              {item}
            </Button>
          ) : (
            <span key={item} className="px-1 text-muted-foreground" aria-hidden>
              …
            </span>
          ),
        )}
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => onPageChange(page + 1)}
          disabled={page >= totalPages}
        >
          Next <ChevronRight className="size-4" aria-hidden />
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => onPageChange(totalPages)}
          disabled={page >= totalPages}
          aria-label="Last page"
        >
          <ChevronsRight className="size-4" aria-hidden />
        </Button>
      </div>
    </nav>
  );
};
