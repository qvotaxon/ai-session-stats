import { QueryClient } from "@tanstack/react-query";
import { isApiError } from "./apiError";

export const createQueryClient = () =>
  new QueryClient({
    defaultOptions: {
      queries: {
        refetchOnWindowFocus: true,
        // A 4xx will not heal by retrying; a dropped connection might.
        retry: (failureCount, error) => (isApiError(error) && error.status !== null ? false : failureCount < 1),
        staleTime: 5_000,
      },
    },
  });

export const queryClient = createQueryClient();
