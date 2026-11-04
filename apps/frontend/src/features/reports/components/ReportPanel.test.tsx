import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { makeRun } from "@/common/testSetup/fixtures";
import { renderWithQuery } from "@/common/testSetup/renderHelpers";
import { showToast } from "@/common/toast";
import { saveBlob } from "@/lib/download";
import { ApiError } from "@/infrastructure/api";
import { reportsApi } from "../api/reportsApi";
import { ReportPanel } from "./ReportPanel";

vi.mock("../api/reportsApi", () => ({
  reportsApi: { list: vi.fn(), create: vi.fn(), cancel: vi.fn(), content: vi.fn(), download: vi.fn() },
}));
vi.mock("@/common/toast", () => ({ showToast: vi.fn() }));
vi.mock("@/lib/download", () => ({ saveBlob: vi.fn() }));

const api = vi.mocked(reportsApi);

describe("ReportPanel", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    api.list.mockResolvedValue([]);
  });

  it("offers to generate when there is no report", async () => {
    renderWithQuery(<ReportPanel sessionId="s-1" />);
    expect(await screen.findByText(/No report yet/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Generate session report" })).toBeEnabled();
  });

  it("generates, disables the button while running, then polls until the markdown report shows", async () => {
    const user = userEvent.setup();
    const running = makeRun({ status: "running", finishedAt: null, durationMs: null });
    api.create.mockResolvedValue(makeRun({ status: "queued" }));
    api.list
      .mockResolvedValueOnce([]) // initial load
      .mockResolvedValueOnce([running]) // after the POST
      .mockResolvedValue([makeRun()]); // the next poll
    api.content.mockResolvedValue("# Hello report\n\n| a | b |\n|---|---|\n| 1 | 2 |");

    renderWithQuery(<ReportPanel sessionId="s-1" />);
    await user.click(await screen.findByRole("button", { name: "Generate session report" }));

    expect(api.create).toHaveBeenCalledWith("s-1");
    const busy = await screen.findByRole("button", { name: "Generating…" });
    expect(busy).toBeDisabled();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeInTheDocument();

    expect(await screen.findByRole("heading", { name: "Hello report" }, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.getByRole("table")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Re-run analysis" })).toBeEnabled();
  }, 10_000);

  it("opens the latest succeeded version automatically and offers re-run and download", async () => {
    const user = userEvent.setup();
    api.list.mockResolvedValue([
      makeRun({ id: "run-3", version: 3, status: "failed", error: "boom" }),
      makeRun({ id: "run-2", version: 2 }),
      makeRun({ id: "run-1", version: 1 }),
    ]);
    api.content.mockImplementation((_s, runId) => Promise.resolve(`# Report from ${runId}`));
    api.download.mockResolvedValue(new Blob(["x"]));

    renderWithQuery(<ReportPanel sessionId="s-1" />);

    expect(await screen.findByRole("heading", { name: "Report from run-2" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Re-run analysis" })).toBeEnabled();

    await user.click(screen.getByRole("button", { name: /Download/ }));
    await waitFor(() => expect(saveBlob).toHaveBeenCalledWith(expect.any(Blob), "session-s-1-v2.md"));
  });

  it("shows the error of a failed run", async () => {
    api.list.mockResolvedValue([makeRun({ status: "failed", error: "CLI exited with code 2" })]);
    renderWithQuery(<ReportPanel sessionId="s-1" />);
    expect(await screen.findByText("CLI exited with code 2")).toBeInTheDocument();
    expect(api.content).not.toHaveBeenCalled();
  });

  it("toasts when a run is already active (409)", async () => {
    const user = userEvent.setup();
    api.create.mockRejectedValue(new ApiError("conflict", 409, "Conflict", null));
    renderWithQuery(<ReportPanel sessionId="s-1" />);

    await user.click(await screen.findByRole("button", { name: "Generate session report" }));

    await waitFor(() =>
      expect(showToast).toHaveBeenCalledWith("warn", "A report is already running", expect.any(String)),
    );
  });

  it("cancels a running run", async () => {
    const user = userEvent.setup();
    const running = makeRun({ status: "running", finishedAt: null });
    api.list.mockResolvedValue([running]);
    api.cancel.mockResolvedValue(makeRun({ status: "cancelled" }));
    renderWithQuery(<ReportPanel sessionId="s-1" />);

    await user.click(await screen.findByRole("button", { name: "Cancel" }));

    await waitFor(() => expect(api.cancel).toHaveBeenCalledWith("s-1", "run-1"));
  });

  it("never renders raw HTML from the report", async () => {
    api.list.mockResolvedValue([makeRun()]);
    api.content.mockResolvedValue('# Title\n\n<b data-testid="raw">bold</b><script>window.__pwned = 1</script>');
    const { container } = renderWithQuery(<ReportPanel sessionId="s-1" />);

    await screen.findByRole("heading", { name: "Title" });
    expect(screen.queryByTestId("raw")).not.toBeInTheDocument();
    expect(container.querySelector("script")).toBeNull();
  });
});
