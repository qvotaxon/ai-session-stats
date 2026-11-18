import { useQuery } from "@tanstack/react-query";
import type { PropsWithChildren } from "react";
import { z } from "zod";
import { Loader } from "@/common/Loader";
import { QueryErrorAlert } from "@/common/QueryErrorAlert";

const startupSchema = z.object({ status: z.enum(["starting", "ready", "failed"]) });

export const StartupGate = ({ children }: PropsWithChildren) => {
  const query = useQuery({
    queryKey: ["startup"],
    queryFn: async ({ signal }) => {
      const response = await fetch("/startup", { signal, cache: "no-store" });
      if (!response.ok) throw new Error("Could not check whether Session Insights is ready.");
      return startupSchema.parse(await response.json());
    },
    refetchInterval: (query) => (query.state.data?.status === "ready" ? false : 1000),
    retry: false,
  });

  if (query.data?.status === "ready") return children;

  return (
    <main className="relative grid min-h-screen place-items-center p-6">
      {query.isError ? (
        <QueryErrorAlert
          title="Waiting for Session Insights"
          error={query.error}
          onRetry={() => void query.refetch()}
        />
      ) : (
        <div role="status" className="space-y-4 text-center">
          <div className="relative h-16">
            <Loader />
          </div>
          <h1 className="text-xl font-semibold">Starting Session Insights</h1>
          <p className="text-muted-foreground">
            {query.data?.status === "failed"
              ? "Initialization failed. Retrying automatically. Check the application logs if this continues."
              : "Preparing your local sessions. The dashboard will open automatically."}
          </p>
        </div>
      )}
    </main>
  );
};
