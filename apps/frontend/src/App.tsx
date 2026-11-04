import { RouterProvider } from "@tanstack/react-router";
import { Suspense } from "react";
import { ErrorBoundary } from "react-error-boundary";
import { Loader } from "@/common/Loader";
import SomethingWentWrong from "@/common/SomethingWentWrong";
import { ThemeProvider } from "@/common/theme/ThemeProvider";
import { Toaster } from "@/components/ui/sonner";
import { TooltipProvider } from "@/components/ui/tooltip";
import { router } from "@/routes/routes";

export const App = () => (
  <ThemeProvider>
    <ErrorBoundary FallbackComponent={SomethingWentWrong}>
      <TooltipProvider>
        <Suspense fallback={<Loader />}>
          <RouterProvider router={router} />
        </Suspense>
      </TooltipProvider>
      <Toaster position="top-right" richColors closeButton />
    </ErrorBoundary>
  </ThemeProvider>
);
