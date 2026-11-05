import { z } from "zod";

export const sourceSchema = z.enum(["copilot", "claude"]);
export const reportStatusSchema = z.enum(["queued", "running", "succeeded", "failed", "cancelled"]);

const count = z.number().default(0);

export const sessionSummarySchema = z.object({
  id: z.string(),
  source: sourceSchema,
  title: z.string(),
  project: z.string().nullable().default(null),
  startedAt: z.string(),
  lastActivityAt: z.string().optional(),
  endedAt: z.string().nullable().default(null),
  durationSeconds: count,
  models: z.array(z.string()).default([]),
  totalTokens: count,
  toolCallCount: count,
  hookCount: count,
  guardCount: count,
  subagentCount: count,
  latestReportStatus: reportStatusSchema.nullable().default(null),
});

export const sessionPageSchema = z.object({
  items: z.array(sessionSummarySchema),
  page: z.number(),
  pageSize: z.number(),
  totalCount: z.number(),
  totalPages: z.number(),
});

const nameCount = z.object({ name: z.string(), count });

export const sessionDetailSchema = sessionSummarySchema.extend({
  recap: z.string().nullable().default(null),
  recapAt: z.string().nullable().default(null),
  tokens: z.object({
    input: count,
    output: count,
    cacheRead: count,
    cacheWrite: count,
    reasoning: count,
    total: count,
  }),
  tokensByModel: z
    .array(
      z.object({
        model: z.string(),
        requests: count,
        input: count,
        output: count,
        cacheRead: count,
        cacheWrite: count,
        reasoning: count,
      }),
    )
    .default([]),
  toolCalls: z.object({
    total: count,
    succeeded: count,
    failed: count,
    byTool: z
      .array(
        z.object({
          name: z.string(),
          count,
          failed: count,
          avgDurationMs: count,
          totalDurationMs: count,
        }),
      )
      .default([]),
  }),
  hooks: z.object({
    total: count,
    failed: count,
    byType: z.array(z.object({ type: z.string(), count, failed: count, avgDurationMs: count })).default([]),
  }),
  guards: z.object({
    total: count,
    blockedHooks: count,
    permissionDenials: count,
    hookFailures: count,
    items: z
      .array(z.object({ kind: z.enum(["blockedHook", "permissionDenial", "hookFailure"]), name: z.string(), count }))
      .default([]),
  }),
  subagents: z.object({ total: count, byType: z.array(nameCount).default([]) }),
  skills: z.array(nameCount).default([]),
  turns: z.object({ user: count, assistant: count }),
  api: z.object({ requestCount: count, totalApiDurationMs: count }),
  cost: z.object({ premiumRequests: count, nanoAiu: count }).nullable().default(null),
  codeChanges: z.object({ linesAdded: count, linesRemoved: count, filesModified: count }).nullable().default(null),
  timeline: z
    .array(z.object({ bucketStart: z.string(), toolCalls: count, hooks: count, guards: count, tokens: count }))
    .default([]),
  notes: z.array(z.string()).default([]),
});

export type SessionSource = z.infer<typeof sourceSchema>;
export type ReportStatus = z.infer<typeof reportStatusSchema>;
export type SessionSummary = z.infer<typeof sessionSummarySchema>;
export type SessionPage = z.infer<typeof sessionPageSchema>;
export type SessionDetail = z.infer<typeof sessionDetailSchema>;

export const sortFieldSchema = z.enum(["lastActivityAt", "startedAt", "totalTokens", "toolCallCount", "durationSeconds"]);
export type SortField = z.infer<typeof sortFieldSchema>;
export type SortDirection = "asc" | "desc";

export interface SessionsQuery {
  page: number;
  pageSize: number;
  source?: SessionSource;
  search?: string;
  sort: SortField;
  direction: SortDirection;
}
