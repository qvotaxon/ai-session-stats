import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { makeSummary } from "@/common/testSetup/fixtures";
import { renderApp } from "@/common/testSetup/renderHelpers";
import { sessionsApi } from "@/features/sessions/api/sessionsApi";
import type { SessionsQuery } from "@/features/sessions/schemas";

vi.mock("@/features/sessions/api/sessionsApi", () => ({ sessionsApi: { list: vi.fn(), get: vi.fn() } }));

const list = vi.mocked(sessionsApi.list);

const pageOf = (query: SessionsQuery) => ({
  items:
    query.page === 1
      ? [
          makeSummary({ id: "a", title: "Alpha session", latestReportStatus: "succeeded" }),
          makeSummary({ id: "b", title: "Beta session", source: "claude" }),
        ]
      : [makeSummary({ id: "c", title: "Gamma session" })],
  page: query.page,
  pageSize: query.pageSize,
  totalCount: 3,
  totalPages: 2,
});

describe("SessionsPage", () => {
  beforeEach(() => {
    list.mockReset();
    list.mockImplementation((query) => Promise.resolve(pageOf(query)));
  });

  it("renders one table row per session with formatted values", async () => {
    renderApp("/");
    const table = await screen.findByRole("table");
    expect(within(table).getByRole("link", { name: "Alpha session" })).toHaveAttribute("href", "/sessions/a");
    expect(within(table).getByRole("link", { name: "Beta session" })).toBeInTheDocument();
    expect(within(table).getAllByText("1.2M")).toHaveLength(2);
    expect(within(table).getAllByText("3m 12s")).toHaveLength(2);
    expect(within(table).getByText("Report ready")).toBeInTheDocument();
    expect(within(table).getByText("Claude")).toBeInTheDocument();
  });

  it("moves to the next page and keeps it in the URL", async () => {
    const user = userEvent.setup();
    const { router } = renderApp("/");
    await screen.findByRole("table");

    await user.click(screen.getByRole("button", { name: /next/i }));

    expect((await screen.findAllByText("Gamma session")).length).toBeGreaterThan(0);
    expect(router.state.location.search).toMatchObject({ page: 2 });
    expect(list).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }), expect.anything());
    expect(screen.getByRole("button", { name: /next/i })).toBeDisabled();
  });

  it("restores state from the URL and falls back on invalid params", async () => {
    renderApp("/?page=2&source=claude&sort=totalTokens&direction=asc&pageSize=999");
    await screen.findByRole("table");
    expect(list).toHaveBeenCalledWith(
      expect.objectContaining({ page: 2, source: "claude", sort: "totalTokens", direction: "asc", pageSize: 25 }),
      expect.anything(),
    );
    expect(screen.getByRole("columnheader", { name: /tokens/i })).toHaveAttribute("aria-sort", "ascending");
  });

  it("sorts by a column, resetting to page 1 and flipping direction on repeat clicks", async () => {
    const user = userEvent.setup();
    const { router } = renderApp("/?page=2");
    await screen.findByRole("table");

    await user.click(screen.getByRole("button", { name: "Sort by Tokens" }));
    await waitFor(() =>
      expect(router.state.location.search).toMatchObject({ sort: "totalTokens", direction: "desc", page: 1 }),
    );

    await user.click(screen.getByRole("button", { name: "Sort by Tokens" }));
    await waitFor(() => expect(router.state.location.search).toMatchObject({ direction: "asc" }));
  });

  it("debounces the search box into the URL", async () => {
    const user = userEvent.setup();
    const { router } = renderApp("/?page=2");
    await screen.findByRole("table");

    await user.type(screen.getByRole("searchbox", { name: "Search sessions" }), "alp");

    await waitFor(() => expect(router.state.location.search).toMatchObject({ search: "alp", page: 1 }));
    expect(list).toHaveBeenLastCalledWith(expect.objectContaining({ search: "alp" }), expect.anything());
  });

  it("shows an empty state", async () => {
    list.mockResolvedValue({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });
    renderApp("/");
    expect(await screen.findByText("No sessions found")).toBeInTheDocument();
  });

  it("shows an error state with retry", async () => {
    const user = userEvent.setup();
    list.mockRejectedValueOnce(new Error("backend down"));
    renderApp("/");
    expect(await screen.findByText("backend down")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByRole("table")).toBeInTheDocument();
  });
});
