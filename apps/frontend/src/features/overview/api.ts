import { useQuery } from "@tanstack/react-query";
import { z } from "zod";
import { apiClient } from "@/infrastructure/api";
import { sessionSummarySchema, sourceSchema } from "@/features/sessions/schemas";

const versionSchema = z.object({
  source: sourceSchema,
  version: z.string(),
  cliVersion: z.string().nullable(),
  sessions: z.number(),
  directInvocations: z.number(),
  activations: z.number(),
  toolCalls: z.number(),
  toolFailures: z.number(),
  hooks: z.number(),
  hookFailures: z.number(),
  sessionIds: z.array(z.string()),
});

export const overviewSchema = z.object({
  generatedAt: z.string(),
  totalSessions: z.number(),
  recentlyActiveSessions: z.number(),
  totalTokens: z.number(),
  toolCalls: z.number(),
  toolFailures: z.number(),
  hooks: z.number(),
  hookFailures: z.number(),
  sessionsWithVersionEvidence: z.number(),
  attributedHealthEvents: z.number(),
  totalHealthEvents: z.number(),
  recentSessions: z.array(sessionSummarySchema),
  harnessVersions: z.array(versionSchema),
});

const inventoriesSchema = z.array(
  z.object({
    source: sourceSchema,
    status: z.enum(["installed", "notInstalled", "unavailable", "error"]),
    checkedAt: z.string(),
    installations: z.array(
      z.object({ version: z.string(), enabled: z.boolean().nullable(), scope: z.string().nullable() }),
    ),
    error: z.string().nullable(),
  }),
);

export type Overview = z.infer<typeof overviewSchema>;

export const overviewApi = {
  async get(signal?: AbortSignal) {
    const { data } = await apiClient.get<unknown>("/overview", { signal });
    return overviewSchema.parse(data);
  },
  async installations(signal?: AbortSignal) {
    const { data } = await apiClient.get<unknown>("/harness/installations", { signal });
    return inventoriesSchema.parse(data);
  },
};

export const useOverview = () =>
  useQuery({
    queryKey: ["overview"],
    queryFn: ({ signal }) => overviewApi.get(signal),
    refetchInterval: 15_000,
  });

export const useHarnessInstallations = () =>
  useQuery({
    queryKey: ["harness", "installations"],
    queryFn: ({ signal }) => overviewApi.installations(signal),
    staleTime: 60_000,
    refetchInterval: 60_000,
  });
