import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { makeSummary } from "@/common/testSetup/fixtures";
import { renderApp } from "@/common/testSetup/renderHelpers";
import { overviewApi, type Overview } from "@/features/overview/api";

vi.spyOn(overviewApi, "get");
vi.spyOn(overviewApi, "installations");

const overview = (): Overview => ({
  generatedAt: "2026-11-12T10:00:00Z",
  totalSessions: 110,
  recentlyActiveSessions: 8,
  totalTokens: 2_000_000,
  toolCalls: 400,
  toolFailures: 12,
  hooks: 50,
  hookFailures: 3,
  sessionsWithVersionEvidence: 2,
  attributedHealthEvents: 10,
  totalHealthEvents: 450,
  recentSessions: [makeSummary()],
  harnessVersions: [
    {
      source: "claude",
      version: "0.26.1",
      cliVersion: "2.1.302",
      sessions: 1,
      directInvocations: 0,
      activations: 2,
      toolCalls: 10,
      toolFailures: 1,
      hooks: 0,
      hookFailures: 0,
      sessionIds: ["s-1"],
    },
  ],
});

describe("Dashboard and harness", () => {
  beforeEach(() => {
    vi.mocked(overviewApi.get).mockReset().mockResolvedValue(overview());
    vi.mocked(overviewApi.installations)
      .mockReset()
      .mockResolvedValue([
        {
          source: "claude",
          status: "installed",
          checkedAt: "2026-11-12T10:00:00Z",
          installations: [{ version: "0.26.1", enabled: true, scope: "user" }],
          error: null,
        },
      ]);
  });

  it("uses the root for the dashboard, labels aggregate scope, and links to sessions", async () => {
    renderApp("/");
    expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    expect(await screen.findByRole("link", { name: "Fix the scheduler" })).toHaveAttribute("href", "/sessions/s-1");
    const sessionsTotal = screen.getByText("Sessions · all indexed history").parentElement!;
    expect(within(sessionsTotal).getByText("110")).toBeInTheDocument();
    expect(screen.getByText("Allowance unavailable")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /all sessions/i })).toHaveAttribute(
      "href",
      expect.stringContaining("/sessions"),
    );
    expect(await screen.findByText("0.26.1 (user)")).toBeInTheDocument();
  });

  it("shows unknown attribution and supporting sessions without treating missing metrics as zero failure rates", async () => {
    const user = userEvent.setup();
    renderApp("/harness");
    const table = await screen.findByRole("table");
    expect(within(table).getByText("1 / 10 (10.0%)")).toBeInTheDocument();
    expect(within(table).getByText("Not attributed")).toBeInTheDocument();
    expect(screen.getByText(/440 tool\/hook events remain unattributed/)).toBeInTheDocument();
    await user.click(within(table).getByText("View sessions"));
    expect(within(table).getByRole("link", { name: "s-1" })).toHaveAttribute("href", "/sessions/s-1");
  });

  it("shows a real empty state and inventory errors independently", async () => {
    vi.mocked(overviewApi.get).mockResolvedValue({
      ...overview(),
      recentSessions: [],
      harnessVersions: [],
      totalSessions: 0,
      sessionsWithVersionEvidence: 0,
    });
    vi.mocked(overviewApi.installations).mockResolvedValue([
      {
        source: "copilot",
        status: "error",
        checkedAt: "2026-11-12T10:00:00Z",
        installations: [],
        error: "Plugin listing timed out.",
      },
    ]);
    renderApp("/");
    expect(await screen.findByText(/No sessions found/)).toBeInTheDocument();
    expect(await screen.findByRole("alert")).toHaveTextContent("Plugin listing timed out.");
  });

  it("surfaces overview failures and supports retry", async () => {
    const user = userEvent.setup();
    vi.mocked(overviewApi.get).mockRejectedValueOnce(new Error("Overview unavailable"));
    renderApp("/");
    expect(await screen.findByText("Overview unavailable")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByRole("link", { name: "Fix the scheduler" })).toBeInTheDocument();
  });
});
