import { z } from "zod";
import { apiClient } from "@/infrastructure/api";

const applicationLogSchema = z.object({
  fileName: z.string().nullable(),
  content: z.string(),
  truncated: z.boolean(),
});

export const logsApi = {
  async get(signal?: AbortSignal) {
    const { data } = await apiClient.get<unknown>("/logs", { signal });
    return applicationLogSchema.parse(data);
  },
};
