import { screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { makeDetail } from "@/common/testSetup/fixtures";
import { renderApp } from "@/common/testSetup/renderHelpers";
import { reportsApi } from "@/features/reports/api/reportsApi";
import { sessionsApi } from "@/features/sessions/api/sessionsApi";
import { ApiError } from "@/infrastructure/api";

vi.mock("@/features/sessions/api/sessionsApi", () => ({ sessionsApi: { list: vi.fn(), get: vi.fn() } }));
vi.mock("@/features/reports/api/reportsApi", () => ({
  reportsApi: { list: vi.fn(), create: vi.fn(), cancel: vi.fn(), content: vi.fn(), download: vi.fn() },
}));

const get = vi.mocked(sessionsApi.get);

// Warm the lazily-loaded route chunk so the first test is not racing the module transform.
beforeAll(() => import("./SessionDetailPage"), 30_000);

describe("SessionDetailPage", () => {
  beforeEach(() => {
    get.mockReset();
    vi.mocked(reportsApi.list).mockResolvedValue([]);
  });

  it("renders the header and KPI cards including cost and code changes", async () => {
    get.mockResolvedValue(makeDetail());
    renderApp("/sessions/s-1");

    expect(await screen.findByRole("heading", { name: "Fix the scheduler" })).toBeInTheDocument();
    const kpis = screen.getByLabelText("Key metrics");
    expect(kpis).toHaveTextContent("Total tokens");
    expect(kpis).toHaveTextContent("1.2M");
    expect(kpis).toHaveTextContent("2 failed (4.8%)");
    expect(kpis).toHaveTextContent("Premium requests");
    expect(kpis).toHaveTextContent("+120");
    expect(kpis).toHaveTextContent("4 files modified");
    expect(screen.getByRole("img", { name: /^Token breakdown\./ })).toBeInTheDocument();
  });

  it("shows 'Not recorded' for API time when requests exist but no duration was recorded", async () => {
    get.mockResolvedValue(makeDetail({ api: { requestCount: 5, totalApiDurationMs: 0 } }));
    renderApp("/sessions/s-1");

    await screen.findByRole("heading", { name: "Fix the scheduler" });
    const kpis = screen.getByLabelText("Key metrics");
    expect(kpis).toHaveTextContent("Not recorded");
    expect(kpis).not.toHaveTextContent("0 ms");
    expect(kpis).toHaveTextContent("5 requests");
  });

  it("shows premium cost in AIU converted from nano AIU", async () => {
    get.mockResolvedValue(makeDetail({ cost: { premiumRequests: 7, nanoAiu: 2_143_600_000_000 } }));
    renderApp("/sessions/s-1");

    await screen.findByRole("heading", { name: "Fix the scheduler" });
    expect(screen.getByLabelText("Key metrics")).toHaveTextContent("2,143.6 AIU");
  });

  it("hides cost and code-change KPIs when null and explains why in an alert", async () => {
    get.mockResolvedValue(
      makeDetail({ cost: null, codeChanges: null, notes: ["Premium requests are not available for this CLI."] }),
    );
    renderApp("/sessions/s-1");

    await screen.findByRole("heading", { name: "Fix the scheduler" });
    const kpis = screen.getByLabelText("Key metrics");
    expect(kpis).not.toHaveTextContent("Premium requests");
    expect(kpis).not.toHaveTextContent("Lines changed");
    expect(screen.getByText("Not available for this CLI")).toBeInTheDocument();
    expect(screen.getByText("Premium requests are not available for this CLI.")).toBeInTheDocument();
  });

  it("shows empty-state placeholders for empty sections", async () => {
    get.mockResolvedValue(
      makeDetail({
        toolCalls: { total: 0, succeeded: 0, failed: 0, byTool: [] },
        hooks: { total: 0, failed: 0, byType: [] },
        guards: { total: 0, blockedHooks: 0, permissionDenials: 0, hookFailures: 0, items: [] },
        subagents: { total: 0, byType: [] },
        timeline: [],
      }),
    );
    renderApp("/sessions/s-1");

    expect(await screen.findByText("No hooks ran in this session.")).toBeInTheDocument();
    expect(screen.getByText("No guards triggered in this session.")).toBeInTheDocument();
    expect(screen.getByText("No subagents were used.")).toBeInTheDocument();
    expect(screen.getByText("No timeline available.")).toBeInTheDocument();
  });

  it("shows a friendly not-found page on 404", async () => {
    get.mockRejectedValue(new ApiError("nope", 404, "Not Found", null));
    renderApp("/sessions/missing");

    expect(await screen.findByRole("heading", { name: "Session not found" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to sessions" })).toBeInTheDocument();
  });
});
