import { apiClient } from "@/infrastructure/api";
import { settingsSchema, type Settings } from "../schemas";

export const settingsApi = {
  async get(signal?: AbortSignal): Promise<Settings> {
    const { data } = await apiClient.get<unknown>("/settings", { signal });
    return settingsSchema.parse(data);
  },

  async setTerminal(terminal: string): Promise<Settings> {
    const { data } = await apiClient.put<unknown>("/settings", { terminal });
    return settingsSchema.parse(data);
  },
};
