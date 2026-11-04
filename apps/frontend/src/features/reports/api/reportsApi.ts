import { apiClient } from "@/infrastructure/api";
import { reportRunListSchema, reportRunSchema, type ReportRun } from "../schemas";

const base = (sessionId: string) => `/sessions/${encodeURIComponent(sessionId)}/reports`;

export const reportsApi = {
  async list(sessionId: string, signal?: AbortSignal): Promise<ReportRun[]> {
    const { data } = await apiClient.get<unknown>(base(sessionId), { signal });
    return reportRunListSchema.parse(data);
  },

  async create(sessionId: string): Promise<ReportRun> {
    const { data } = await apiClient.post<unknown>(base(sessionId));
    return reportRunSchema.parse(data);
  },

  async cancel(sessionId: string, runId: string): Promise<ReportRun> {
    const { data } = await apiClient.post<unknown>(`${base(sessionId)}/${encodeURIComponent(runId)}/cancel`);
    return reportRunSchema.parse(data);
  },

  async content(sessionId: string, runId: string, signal?: AbortSignal): Promise<string> {
    const { data } = await apiClient.get<string>(`${base(sessionId)}/${encodeURIComponent(runId)}/content`, {
      responseType: "text",
      transformResponse: (raw: string) => raw,
      signal,
    });
    return data;
  },

  /** Fetched through the typed client (token header) as a blob; the caller saves it via an object URL. */
  async download(sessionId: string, runId: string): Promise<Blob> {
    const { data } = await apiClient.get<Blob>(`${base(sessionId)}/${encodeURIComponent(runId)}/download`, {
      responseType: "blob",
    });
    return data;
  },
};
