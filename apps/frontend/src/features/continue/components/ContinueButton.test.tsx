import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithQuery } from "@/common/testSetup/renderHelpers";
import { showToast } from "@/common/toast";
import { ApiError } from "@/infrastructure/api";
import { TooltipProvider } from "@/components/ui/tooltip";
import { settingsApi } from "@/features/settings/api/settingsApi";
import { continueApi } from "../api/continueApi";
import { ContinueButton } from "./ContinueButton";

vi.mock("../api/continueApi", () => ({ continueApi: { start: vi.fn() } }));
vi.mock("@/features/settings/api/settingsApi", () => ({ settingsApi: { get: vi.fn(), setTerminal: vi.fn() } }));
vi.mock("@/common/toast", () => ({ showToast: vi.fn() }));

const api = vi.mocked(continueApi);
const settings = vi.mocked(settingsApi);

const renderButton = (canContinue = true) =>
  renderWithQuery(
    <TooltipProvider>
      <ContinueButton sessionId="s-1" canContinue={canContinue} />
    </TooltipProvider>,
  );

describe("ContinueButton", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    settings.get.mockResolvedValue({
      terminal: "wt-pwsh",
      terminals: [{ id: "wt-pwsh", label: "Windows Terminal + PowerShell 7", available: true }],
    });
  });

  it("opens the terminal without forcing", async () => {
    const user = userEvent.setup();
    api.start.mockResolvedValue();
    renderButton();

    await user.click(screen.getByRole("button", { name: /Continue in CLI/ }));

    expect(api.start).toHaveBeenCalledWith("s-1", false);
    await waitFor(() => expect(showToast).toHaveBeenCalledWith("success", "Terminal opened", expect.any(String)));
  });

  it("asks for confirmation when the session looks active, then forces", async () => {
    const user = userEvent.setup();
    api.start
      .mockRejectedValueOnce(new ApiError("active", 409, "Conflict", "active", "session_active"))
      .mockResolvedValueOnce();
    renderButton();

    await user.click(screen.getByRole("button", { name: /Continue in CLI/ }));
    expect(await screen.findByText("This session looks active")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Continue anyway" }));

    await waitFor(() => expect(api.start).toHaveBeenLastCalledWith("s-1", true));
  });

  it("does not offer to force when a report is running", async () => {
    const user = userEvent.setup();
    api.start.mockRejectedValue(new ApiError("report", 409, "Conflict", "A report is running", null));
    renderButton();

    await user.click(screen.getByRole("button", { name: /Continue in CLI/ }));

    await waitFor(() => expect(showToast).toHaveBeenCalledWith("warn", "Cannot continue yet", "A report is running"));
    expect(screen.queryByText("This session looks active")).not.toBeInTheDocument();
  });

  it("disables continuing and terminal choices while saving a terminal choice", async () => {
    const user = userEvent.setup();
    settings.get.mockResolvedValue({
      terminal: "wt-pwsh",
      terminals: [
        { id: "wt-pwsh", label: "Windows Terminal + PowerShell 7", available: true },
        { id: "cmd", label: "Command Prompt", available: true },
      ],
    });
    settings.setTerminal.mockImplementation(() => new Promise(() => {}));
    renderButton();

    await user.click(screen.getByRole("button", { name: "Choose terminal" }));
    await user.click(await screen.findByRole("menuitemradio", { name: "Command Prompt" }));

    await waitFor(() => {
      expect(screen.getByRole("button", { name: /Continue in CLI/ })).toBeDisabled();
      expect(screen.getByRole("button", { name: "Choose terminal" })).toBeDisabled();
    });
    expect(settings.setTerminal).toHaveBeenCalledWith("cmd");
  });

  it("is disabled when the working folder is gone", () => {
    renderButton(false);
    expect(screen.getByRole("button", { name: /Continue in CLI/ })).toBeDisabled();
  });
});
