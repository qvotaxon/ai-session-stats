import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { showToast } from "@/common/toast";
import { toastApiError } from "@/common/toastApiError";
import { isApiError } from "@/infrastructure/api";
import { continueApi } from "../api/continueApi";

const SESSION_ACTIVE = "session_active";

/** Opens the session in a terminal. A recently active session asks for confirmation before it is forced. */
export const useContinueSession = (sessionId: string) => {
  const [confirmOpen, setConfirmOpen] = useState(false);

  const mutation = useMutation({
    mutationFn: (force: boolean) => continueApi.start(sessionId, force),
    onSuccess: () => {
      setConfirmOpen(false);
      showToast("success", "Terminal opened", "The session is resuming in a new window.");
    },
    onError: (error) => {
      if (isApiError(error, 409) && error.code === SESSION_ACTIVE) {
        setConfirmOpen(true);
      } else if (isApiError(error, 409)) {
        showToast("warn", "Cannot continue yet", error.detail ?? undefined);
      } else {
        toastApiError(error, "Could not open the terminal");
      }
    },
  });

  return {
    continueSession: () => mutation.mutate(false),
    forceContinue: () => mutation.mutate(true),
    isPending: mutation.isPending,
    confirmOpen,
    setConfirmOpen,
  };
};
