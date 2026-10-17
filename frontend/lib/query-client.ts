import { QueryClient } from "@tanstack/react-query";
import { isServiceUnavailable } from "./api";

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      // An open circuit breaker answers instantly with 503 — retrying just adds noise.
      retry: (failureCount, error) => !isServiceUnavailable(error) && failureCount < 1,
    },
  },
});
