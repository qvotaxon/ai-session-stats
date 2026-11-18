import { z } from "zod";
import { apiClient } from "@/infrastructure/api";

const projectsSchema = z.array(z.object({ id: z.string(), name: z.string(), sessionCount: z.number() }));
const analysisSchema = z.object({
  sessionsScanned: z.number(),
  requestsAnalyzed: z.number(),
  sampled: z.boolean(),
  opportunities: z.array(
    z.object({
      id: z.string(),
      title: z.string(),
      outline: z.string(),
      instructions: z.string(),
      occurrences: z.number(),
      sessionCount: z.number(),
      evidence: z.array(z.object({ sessionId: z.string(), title: z.string(), occurrences: z.number() })),
    }),
  ),
});

export type AnalysisFilters = ({ sessionId: string } | { projectId: string }) & {
  from?: string;
  to?: string;
  cli: "claude" | "copilot";
  consent: boolean;
};

export const evolveApi = {
  async projects(signal?: AbortSignal) {
    const { data } = await apiClient.get<unknown>("/evolve/projects", { signal });
    return projectsSchema.parse(data);
  },
  async analyze(params: AnalysisFilters) {
    const { data } = await apiClient.post<unknown>("/evolve/analysis", params, { timeout: 150_000 });
    return analysisSchema.parse(data);
  },
};
