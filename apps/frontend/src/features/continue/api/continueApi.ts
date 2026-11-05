import { apiClient } from "@/infrastructure/api";

export const continueApi = {
  async start(sessionId: string, force: boolean): Promise<void> {
    await apiClient.post(`/sessions/${encodeURIComponent(sessionId)}/continue`, { force });
  },
};
