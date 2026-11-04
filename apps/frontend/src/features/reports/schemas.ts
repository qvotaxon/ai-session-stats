import { z } from "zod";
import { reportStatusSchema, sourceSchema } from "@/features/sessions/schemas";

export const reportRunSchema = z.object({
  id: z.string(),
  sessionId: z.string(),
  version: z.number(),
  status: reportStatusSchema,
  cli: sourceSchema,
  forked: z.boolean().default(false),
  forkedSessionId: z.string().nullable().default(null),
  startedAt: z.string(),
  finishedAt: z.string().nullable().default(null),
  durationMs: z.number().nullable().default(null),
  exitCode: z.number().nullable().default(null),
  error: z.string().nullable().default(null),
});

export const reportRunListSchema = z.array(reportRunSchema);

export type ReportRun = z.infer<typeof reportRunSchema>;

export const isActiveRun = (run: ReportRun): boolean => run.status === "queued" || run.status === "running";
