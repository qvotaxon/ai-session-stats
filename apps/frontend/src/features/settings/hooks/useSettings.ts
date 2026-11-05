import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toastApiError } from "@/common/toastApiError";
import { settingsApi } from "../api/settingsApi";

export const settingsKey = ["settings"] as const;

export const useSettings = () => useQuery({ queryKey: settingsKey, queryFn: ({ signal }) => settingsApi.get(signal) });

export const useSetTerminal = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (terminal: string) => settingsApi.setTerminal(terminal),
    onSuccess: (settings) => queryClient.setQueryData(settingsKey, settings),
    onError: (error) => toastApiError(error, "Could not save the terminal choice"),
  });
};
