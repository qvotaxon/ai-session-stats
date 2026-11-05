import { flexRender, getCoreRowModel, useReactTable, createColumnHelper } from "@tanstack/react-table";
import { ArrowDown, ArrowUp, ArrowUpDown } from "lucide-react";
import { useMemo } from "react";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ReportStatusBadge } from "@/features/reports/components/ReportStatusBadge";
import { formatInteger } from "@/lib/format";
import type { SessionSummary, SortDirection, SortField } from "../schemas";
import { Duration, ModelsCell, SessionTitleLink, StartedAt, Tokens } from "./SessionCells";
import { SourceBadge } from "./SourceBadge";

const columnHelper = createColumnHelper<SessionSummary>();

interface SortableHeaderProps {
  label: string;
  field: SortField;
  sort: SortField;
  direction: SortDirection;
  onSort: (field: SortField) => void;
}

const SortableHeader = ({ label, field, sort, direction, onSort }: SortableHeaderProps) => {
  const active = sort === field;
  const Icon = !active ? ArrowUpDown : direction === "asc" ? ArrowUp : ArrowDown;
  return (
    <Button
      variant="ghost"
      size="sm"
      className="-ml-3 h-8 px-3 font-semibold"
      onClick={() => onSort(field)}
      aria-label={`Sort by ${label}`}
    >
      {label}
      <Icon className="size-3.5" aria-hidden />
    </Button>
  );
};

interface SessionsTableProps {
  sessions: SessionSummary[];
  sort: SortField;
  direction: SortDirection;
  onSort: (field: SortField) => void;
}

export const SessionsTable = ({ sessions, sort, direction, onSort }: SessionsTableProps) => {
  const columns = useMemo(() => {
    const header = (label: string, field: SortField) => () => (
      <SortableHeader label={label} field={field} sort={sort} direction={direction} onSort={onSort} />
    );
    return [
      columnHelper.accessor("source", { header: "Source", cell: (c) => <SourceBadge source={c.getValue()} /> }),
      columnHelper.accessor("title", {
        header: "Title",
        cell: (c) => (
          <div className="min-w-[12rem] max-w-[16rem]">
            <SessionTitleLink session={c.row.original} />
          </div>
        ),
      }),
      columnHelper.accessor("project", {
        header: "Project",
        cell: (c) => <span className="block max-w-[8rem] truncate">{c.getValue() ?? "–"}</span>,
      }),
      columnHelper.accessor((row) => row.lastActivityAt ?? row.startedAt, {
        id: "lastActivityAt",
        header: header("Last active", "lastActivityAt"),
        cell: (c) => <StartedAt iso={c.getValue()} />,
      }),
      columnHelper.accessor("startedAt", {
        header: header("Started", "startedAt"),
        cell: (c) => <StartedAt iso={c.getValue()} />,
      }),
      columnHelper.accessor("durationSeconds", {
        header: header("Duration", "durationSeconds"),
        cell: (c) => <Duration seconds={c.getValue()} />,
      }),
      columnHelper.accessor("models", { header: "Models", cell: (c) => <ModelsCell models={c.getValue()} /> }),
      columnHelper.accessor("totalTokens", {
        header: header("Tokens", "totalTokens"),
        cell: (c) => <Tokens value={c.getValue()} />,
      }),
      columnHelper.accessor("toolCallCount", {
        header: header("Tools", "toolCallCount"),
        cell: (c) => formatInteger(c.getValue()),
      }),
      columnHelper.accessor("hookCount", { header: "Hooks", cell: (c) => formatInteger(c.getValue()) }),
      columnHelper.accessor("guardCount", { header: "Guards", cell: (c) => formatInteger(c.getValue()) }),
      columnHelper.accessor("subagentCount", { header: "Subagents", cell: (c) => formatInteger(c.getValue()) }),
      columnHelper.accessor("latestReportStatus", {
        header: "Report",
        cell: (c) => <ReportStatusBadge status={c.getValue()} />,
      }),
    ];
  }, [sort, direction, onSort]);

  // TanStack Table returns unstable function references, so the React Compiler cannot memoize it safely.
  // eslint-disable-next-line react-hooks/incompatible-library
  const table = useReactTable({
    data: sessions,
    columns,
    getCoreRowModel: getCoreRowModel(),
    getRowId: (row) => row.id,
    manualSorting: true,
  });

  const ariaSort = (columnId: string): "ascending" | "descending" | "none" | undefined =>
    columnId === sort ? (direction === "asc" ? "ascending" : "descending") : undefined;

  return (
    <div className="rounded-lg border bg-card">
      <Table>
        <TableHeader>
          {table.getHeaderGroups().map((group) => (
            <TableRow key={group.id}>
              {group.headers.map((h) => (
                <TableHead key={h.id} aria-sort={ariaSort(h.column.id)} className="whitespace-nowrap">
                  {flexRender(h.column.columnDef.header, h.getContext())}
                </TableHead>
              ))}
            </TableRow>
          ))}
        </TableHeader>
        <TableBody>
          {table.getRowModel().rows.map((row) => (
            <TableRow key={row.id}>
              {row.getVisibleCells().map((cell) => (
                <TableCell key={cell.id} className="align-top">
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
};
