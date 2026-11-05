import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { sessionsApi } from "../api/sessionsApi";
import type { SessionsQuery } from "../schemas";

// Matches the backend index refresh interval; TanStack pauses it while the tab is hidden.
const LIVE_REFRESH_MS = 15_000;

export const sessionKeys = {
  all: ["sessions"] as const,
  list: (query: SessionsQuery) => ["sessions", "list", query] as const,
  detail: (id: string) => ["sessions", "detail", id] as const,
};

export const useSessionsList = (query: SessionsQuery) =>
  useQuery({
    queryKey: sessionKeys.list(query),
    queryFn: ({ signal }) => sessionsApi.list(query, signal),
    placeholderData: keepPreviousData,
    refetchInterval: LIVE_REFRESH_MS,
  });

export const useSessionDetail = (id: string) =>
  useQuery({
    queryKey: sessionKeys.detail(id),
    queryFn: ({ signal }) => sessionsApi.get(id, signal),
    refetchInterval: LIVE_REFRESH_MS,
  });
