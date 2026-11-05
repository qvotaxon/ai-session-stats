import { z } from "zod";
import { sortFieldSchema } from "./schemas";

export const PAGE_SIZES = [10, 25, 50, 100] as const;

/** URL state of the master list. Invalid or missing values fall back to defaults so a hand-edited URL never breaks the page. */
export const sessionsSearchSchema = z.object({
  page: z.number().int().min(1).catch(1),
  pageSize: z
    .number()
    .refine((value) => (PAGE_SIZES as readonly number[]).includes(value))
    .catch(25),
  source: z.enum(["all", "copilot", "claude"]).catch("all"),
  // TanStack Router JSON-parses search values, so "123" arrives as a number.
  search: z.union([z.string(), z.number()]).transform(String).catch(""),
  sort: sortFieldSchema.catch("lastActivityAt"),
  direction: z.enum(["asc", "desc"]).catch("desc"),
});

export type SessionsSearch = z.infer<typeof sessionsSearchSchema>;

export const DEFAULT_SESSIONS_SEARCH: SessionsSearch = {
  page: 1,
  pageSize: 25,
  source: "all",
  search: "",
  sort: "lastActivityAt",
  direction: "desc",
};
