import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { z } from "zod";
import { apiClient } from "@/infrastructure/api";
import { sessionSummarySchema, sourceSchema } from "@/features/sessions/schemas";
import { toastApiError } from "@/common/toastApiError";

const harnessUpdateSchema = z.object({
  source: sourceSchema,
  status: z.enum(["pending", "checking", "checked", "updating", "succeeded", "error"]),
  latestVersion: z.string().nullable(),
  available: z.boolean(),
  checkedAt: z.string().nullable(),
  completedAt: z.string().nullable(),
  completionStatus: z.enum(["succeeded", "error"]).nullable(),
  completionError: z.string().nullable(),
  error: z.string().nullable(),
});

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

const allowanceWindowSchema = z.object({
  usedPercentage: z.number(),
  resetsAt: z.string().nullable(),
});

const claudeAllowanceSchema = z.object({
  status: z.enum(["available", "stale", "unavailable", "error"]),
  updatedAt: z.string().nullable(),
  fiveHour: allowanceWindowSchema.nullable(),
  sevenDay: allowanceWindowSchema.nullable(),
  message: z.string().nullable(),
});

export type Overview = z.infer<typeof overviewSchema>;
export type ClaudeAllowance = z.infer<typeof claudeAllowanceSchema>;

export const overviewApi = {
  async get(signal?: AbortSignal) {
    const { data } = await apiClient.get<unknown>("/overview", { signal });
    return overviewSchema.parse(data);
  },
  async installations(signal?: AbortSignal) {
    const { data } = await apiClient.get<unknown>("/harness/installations", { signal });
    return inventoriesSchema.parse(data);
  },
  async claudeAllowance(signal?: AbortSignal) {
    const { data } = await apiClient.get<unknown>("/claude/allowance", { signal });
    return claudeAllowanceSchema.parse(data);
  },
  async updates(signal?: AbortSignal) {
    const { data } = await apiClient.get<unknown>("/harness/updates", { signal });
    return z.array(harnessUpdateSchema).parse(data);
  },
  async update(source: z.infer<typeof sourceSchema>) {
    const { data } = await apiClient.post<unknown>(`/harness/updates/${source}`);
    return harnessUpdateSchema.parse(data);
  },
  async checkUpdates() {
    await apiClient.post("/harness/updates/check");
  },
};

export const useHarnessUpdates = () =>
  useQuery({
    queryKey: ["harness", "updates"],
    queryFn: ({ signal }) => overviewApi.updates(signal),
    refetchInterval: 2_000,
  });

export const useUpdateHarness = () => {
  const client = useQueryClient();
  return useMutation({
    mutationFn: overviewApi.update,
    onSuccess: () => client.invalidateQueries({ queryKey: ["harness"] }),
    onError: (error) => toastApiError(error, "Could not start the plugin update"),
  });
};

export const useCheckHarnessUpdates = () => {
  const client = useQueryClient();
  return useMutation({
    mutationFn: overviewApi.checkUpdates,
    onSuccess: () => client.invalidateQueries({ queryKey: ["harness", "updates"] }),
    onError: (error) => toastApiError(error, "Could not check for plugin updates"),
  });
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
