import { QueryClientProvider } from "@tanstack/react-query";
import { render, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createTestQueryClient } from "@/common/testSetup/renderHelpers";
import { showToast } from "@/common/toast";
import { overviewApi } from "./api";
import { HarnessInstallations } from "./HarnessInstallations";

vi.mock("@/common/toast", () => ({ showToast: vi.fn() }));
vi.spyOn(overviewApi, "installations");
vi.spyOn(overviewApi, "updates");

describe("Harness installation completion notifications", () => {
  beforeEach(() => {
    vi.mocked(showToast).mockClear();
    vi.mocked(overviewApi.installations).mockResolvedValue([]);
  });

  it.each([
    ["checking", "error", null, "Installation failed."],
    ["checked", "error", null, "Installation failed."],
    ["error", "succeeded", "Marketplace check failed.", null],
    ["updating", "succeeded", null, null],
  ] as const)("uses the saved %s completion outcome %s", async (status, completionStatus, error, completionError) => {
    vi.mocked(overviewApi.updates).mockResolvedValue([{
      source: "copilot",
      status,
      latestVersion: "1.1.0",
      available: false,
      checkedAt: "2026-11-18T10:00:00Z",
      completedAt: `2026-11-18T09:00:0${["checking", "checked", "error", "updating"].indexOf(status)}Z`,
      error,
      completionStatus,
      completionError,
    }]);
    const client = createTestQueryClient();
    const invalidate = vi.spyOn(client, "invalidateQueries");
    const view = render(<QueryClientProvider client={client}><HarnessInstallations /></QueryClientProvider>);

    await waitFor(() => expect(showToast).toHaveBeenCalledWith(
      completionStatus === "error" ? "error" : "success",
      `copilot MSDC harness ${completionStatus === "error" ? "update failed" : "updated"}`,
      completionError ?? "Restart the CLI to use the new version.",
    ));
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["harness", "installations"] });
    view.unmount();
    render(<QueryClientProvider client={client}><HarnessInstallations /></QueryClientProvider>);
    await waitFor(() => expect(overviewApi.updates).toHaveBeenCalled());
    expect(showToast).toHaveBeenCalledTimes(1);
  });
});
