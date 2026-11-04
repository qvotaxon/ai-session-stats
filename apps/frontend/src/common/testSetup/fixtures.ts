import type { ReportRun } from "@/features/reports/schemas";
import type { SessionDetail, SessionSummary } from "@/features/sessions/schemas";

export const makeSummary = (overrides: Partial<SessionSummary> = {}): SessionSummary => ({
  id: "s-1",
  source: "copilot",
  title: "Fix the scheduler",
  project: "C:\\git\\demo",
  startedAt: "2026-11-04T08:00:00Z",
  endedAt: "2026-11-04T08:03:12Z",
  durationSeconds: 192,
  models: ["gpt-5"],
  totalTokens: 1_234_567,
  toolCallCount: 42,
  hookCount: 3,
  guardCount: 1,
  subagentCount: 2,
  latestReportStatus: null,
  ...overrides,
});

export const makeDetail = (overrides: Partial<SessionDetail> = {}): SessionDetail => ({
  ...makeSummary(),
  tokens: {
    input: 600_000,
    output: 100_000,
    cacheRead: 500_000,
    cacheWrite: 20_000,
    reasoning: 14_567,
    total: 1_234_567,
  },
  tokensByModel: [
    {
      model: "gpt-5",
      requests: 10,
      input: 600_000,
      output: 100_000,
      cacheRead: 500_000,
      cacheWrite: 20_000,
      reasoning: 0,
    },
  ],
  toolCalls: {
    total: 42,
    succeeded: 40,
    failed: 2,
    byTool: [{ name: "bash", count: 20, failed: 2, avgDurationMs: 800, totalDurationMs: 16_000 }],
  },
  hooks: { total: 3, failed: 0, byType: [{ type: "preToolUse", count: 3, failed: 0, avgDurationMs: 100 }] },
  guards: {
    total: 1,
    blockedHooks: 1,
    permissionDenials: 0,
    hookFailures: 0,
    items: [{ kind: "blockedHook", name: "x", count: 1 }],
  },
  subagents: { total: 2, byType: [{ name: "explore", count: 2 }] },
  skills: [],
  turns: { user: 3, assistant: 9 },
  api: { requestCount: 10, totalApiDurationMs: 65_000 },
  cost: { premiumRequests: 7, nanoAiu: 1000 },
  codeChanges: { linesAdded: 120, linesRemoved: 30, filesModified: 4 },
  timeline: [{ bucketStart: "2026-11-04T08:00:00Z", toolCalls: 2, hooks: 1, guards: 0, tokens: 1000 }],
  notes: [],
  ...overrides,
});

export const makeRun = (overrides: Partial<ReportRun> = {}): ReportRun => ({
  id: "run-1",
  sessionId: "s-1",
  version: 1,
  status: "succeeded",
  cli: "copilot",
  forked: false,
  forkedSessionId: null,
  startedAt: "2026-11-04T09:00:00Z",
  finishedAt: "2026-11-04T09:03:12Z",
  durationMs: 192_000,
  exitCode: 0,
  error: null,
  ...overrides,
});
