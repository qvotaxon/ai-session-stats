import { showToast } from "@/common/toast";
import { isApiError } from "@/infrastructure/api";

/** Shows a toast for any failure, preferring the ProblemDetails text. */
export const toastApiError = (error: unknown, fallbackTitle: string): void => {
  if (isApiError(error)) {
    showToast("error", fallbackTitle, error.detail ?? error.title);
  } else {
    showToast("error", fallbackTitle, error instanceof Error ? error.message : undefined);
  }
};
