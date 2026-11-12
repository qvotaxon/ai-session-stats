import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderApp } from "@/common/testSetup/renderHelpers";
import { logsApi } from "@/features/application-log/api/logsApi";
import { settingsApi } from "@/features/settings/api/settingsApi";

vi.mock("@/features/application-log/api/logsApi", () => ({ logsApi: { get: vi.fn() } }));
vi.mock("@/features/settings/api/settingsApi", () => ({ settingsApi: { get: vi.fn() } }));

const getLog = vi.mocked(logsApi.get);

describe("Application Log", () => {
  beforeEach(() => {
    getLog.mockReset();
    getLog.mockResolvedValue({
      fileName: "session-insights-20261112.log",
      content: "[ERR] Session indexing failed\n   at Example()",
      truncated: false,
    });
    vi.mocked(settingsApi.get).mockResolvedValue({ terminal: "cmd", terminals: [] });
  });

  it("is linked only inside the Options menu and navigates to the log page", async () => {
    const user = userEvent.setup();
    const { router } = renderApp("/settings");
    await screen.findByRole("heading", { name: "Settings", level: 1 });
    expect(screen.queryByRole("link", { name: "Application Log" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Options" }));
    await user.click(await screen.findByRole("menuitem", { name: "Application Log" }));

    expect(await screen.findByRole("heading", { name: "Application Log" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/application-log");
    expect(await screen.findByLabelText("Application log contents")).toHaveTextContent("Session indexing failed");
  });

  it("refreshes the log and copies the displayed diagnostics", async () => {
    const user = userEvent.setup();
    renderApp("/application-log");
    await screen.findByLabelText("Application log contents");
    await user.click(screen.getByRole("button", { name: "Copy log" }));
    expect(await navigator.clipboard.readText()).toBe("[ERR] Session indexing failed\n   at Example()");
    expect(await screen.findByRole("status")).toHaveTextContent("Log copied.");

    getLog.mockResolvedValue({
      fileName: "session-insights-20261112.log",
      content: "[WRN] Could not load the settings file",
      truncated: true,
    });
    await user.click(screen.getByRole("button", { name: "Refresh" }));

    await waitFor(() => expect(screen.getByLabelText("Application log contents")).toHaveTextContent("settings file"));
    expect(screen.getByText(/Showing only the last 256 KiB/)).toBeInTheDocument();
  });

  it("shows an empty state when no logs exist", async () => {
    getLog.mockResolvedValue({ fileName: null, content: "", truncated: false });
    renderApp("/application-log");

    expect(await screen.findByText("No application log entries available.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Copy log" })).toBeDisabled();
  });

  it("shows read failures and retries instead of showing an empty log", async () => {
    const user = userEvent.setup();
    getLog.mockRejectedValueOnce(new Error("Cannot read log file"));
    renderApp("/application-log");
    expect(await screen.findByText("Cannot read log file")).toBeInTheDocument();
    expect(screen.queryByText("No application log entries available.")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByLabelText("Application log contents")).toHaveTextContent("Session indexing failed");
  });

  it("reports clipboard failures without hiding the log", async () => {
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, "writeText").mockRejectedValueOnce(new Error("Clipboard denied"));
    renderApp("/application-log");
    await screen.findByLabelText("Application log contents");

    await user.click(screen.getByRole("button", { name: "Copy log" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Could not copy the log");
    expect(screen.getByLabelText("Application log contents")).toBeInTheDocument();
  });
});
