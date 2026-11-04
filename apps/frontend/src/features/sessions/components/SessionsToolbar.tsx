import { Search } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { PAGE_SIZES, type SessionsSearch } from "../searchParams";

const SEARCH_DEBOUNCE_MS = 300;

interface SessionsToolbarProps {
  search: string;
  source: SessionsSearch["source"];
  pageSize: number;
  onSearchChange: (value: string) => void;
  onSourceChange: (value: SessionsSearch["source"]) => void;
  onPageSizeChange: (value: number) => void;
}

const isSource = (value: string): value is SessionsSearch["source"] =>
  value === "all" || value === "copilot" || value === "claude";

export const SessionsToolbar = ({
  search,
  source,
  pageSize,
  onSearchChange,
  onSourceChange,
  onPageSizeChange,
}: SessionsToolbarProps) => {
  const [draft, setDraft] = useState(search);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  useEffect(() => () => clearTimeout(timer.current), []);

  const handleSearchInput = (value: string) => {
    setDraft(value);
    clearTimeout(timer.current);
    timer.current = setTimeout(() => onSearchChange(value), SEARCH_DEBOUNCE_MS);
  };
  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
      <div className="relative flex-1">
        <Search className="pointer-events-none absolute top-2.5 left-3 size-4 text-muted-foreground" aria-hidden />
        <Input
          type="search"
          value={draft}
          onChange={(event) => handleSearchInput(event.target.value)}
          placeholder="Search title, project or id…"
          aria-label="Search sessions"
          className="pl-9"
        />
      </div>
      <div className="flex gap-3">
        <Select value={source} onValueChange={(value) => isSource(value) && onSourceChange(value)}>
          <SelectTrigger className="w-36" aria-label="Filter by source">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All sources</SelectItem>
            <SelectItem value="copilot">Copilot</SelectItem>
            <SelectItem value="claude">Claude</SelectItem>
          </SelectContent>
        </Select>
        <Select value={String(pageSize)} onValueChange={(value) => onPageSizeChange(Number(value))}>
          <SelectTrigger className="w-32" aria-label="Rows per page">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {PAGE_SIZES.map((size) => (
              <SelectItem key={size} value={String(size)}>
                {size} / page
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
    </div>
  );
};
