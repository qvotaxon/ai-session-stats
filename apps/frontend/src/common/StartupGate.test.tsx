import { screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderWithQuery } from "@/common/testSetup/renderHelpers";
import { StartupGate } from "./StartupGate";

afterEach(() => vi.unstubAllGlobals());

describe("StartupGate", () => {
  it("keeps the dashboard unmounted until initialization completes", async () => {
    let ready = false;
    const fetchStatus = vi.fn(async () => ({
      ok: true,
      json: async () => ({ status: ready ? "ready" : "starting" }),
    }));
    vi.stubGlobal("fetch", fetchStatus);
    renderWithQuery(
      <StartupGate>
        <h1>Dashboard</h1>
      </StartupGate>,
    );
    expect(await screen.findByRole("status")).toHaveTextContent("Starting Session Insights");
    expect(screen.queryByRole("heading", { name: "Dashboard" })).not.toBeInTheDocument();
    ready = true;
    await waitFor(() => expect(screen.getByRole("heading", { name: "Dashboard" })).toBeInTheDocument(), {
      timeout: 3000,
    });
  });

  it("explains initialization failures instead of showing an endless spinner alone", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, json: async () => ({ status: "failed" }) }));
    renderWithQuery(<StartupGate>Dashboard</StartupGate>);
    expect(await screen.findByText(/Initialization failed/)).toBeInTheDocument();
    expect(screen.queryByText("Dashboard")).not.toBeInTheDocument();
  });

  it("reports readiness request failures", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false }));
    renderWithQuery(<StartupGate>Dashboard</StartupGate>);
    expect(await screen.findByText("Could not check whether Session Insights is ready.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });
});
