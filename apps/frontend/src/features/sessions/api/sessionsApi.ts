import { apiClient } from "@/infrastructure/api";
import {
  sessionDetailSchema,
  sessionPageSchema,
  type SessionDetail,
  type SessionPage,
  type SessionsQuery,
} from "../schemas";

export const sessionsApi = {
  async list(query: SessionsQuery, signal?: AbortSignal): Promise<SessionPage> {
    const { data } = await apiClient.get<unknown>("/sessions", {
      params: {
        page: query.page,
        pageSize: query.pageSize,
        source: query.source,
        search: query.search || undefined,
        sort: query.sort,
        direction: query.direction,
      },
      signal,
    });
    return sessionPageSchema.parse(data);
  },

  async get(id: string, signal?: AbortSignal): Promise<SessionDetail> {
    const { data } = await apiClient.get<unknown>(`/sessions/${encodeURIComponent(id)}`, { signal });
    return sessionDetailSchema.parse(data);
  },
};
