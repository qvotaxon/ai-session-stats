import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { renderApp } from "@/common/testSetup/renderHelpers";
import { settingsApi } from "@/features/settings/api/settingsApi";

vi.mock("@/features/settings/api/settingsApi", () => ({ settingsApi: { get: vi.fn() } }));
vi.mock("../../../../CHANGELOG.md?raw", () => ({
  default:
    "# Changelog\n\n## [2.0.0](https://github.com/qvotaxon/ai-session-stats/releases/tag/v2.0.0) (2026-11-12)\n\n### Features\n\n* Offline release history\n",
}));

beforeAll(() => import("./ChangelogPage"), 30_000);

describe("Changelog", () => {
  beforeEach(() => {
    vi.mocked(settingsApi.get).mockResolvedValue({ terminal: "cmd", terminals: [] });
  });

  it("renders bundled Markdown on a direct visit and links back to settings", async () => {
    const user = userEvent.setup();
    const { router } = renderApp("/changelog");

    expect(await screen.findByRole("heading", { name: "Changelog", level: 1 })).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    expect(screen.getByRole("heading", { name: "2.0.0 (2026-11-12)", level: 2 })).toBeInTheDocument();
    expect(screen.getByRole("listitem")).toHaveTextContent("Offline release history");
    const releaseLink = screen.getByRole("link", { name: "2.0.0" });
    expect(releaseLink).toHaveAttribute("href", "https://github.com/qvotaxon/ai-session-stats/releases/tag/v2.0.0");
    expect(releaseLink).toHaveAttribute("rel", "noopener noreferrer");

    await user.click(screen.getByRole("link", { name: "Back to settings" }));
    expect(await screen.findByRole("heading", { name: "Settings", level: 1 })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/settings");
  });

  it("opens from the settings page even when terminal settings cannot load", async () => {
    vi.mocked(settingsApi.get).mockRejectedValue(new Error("Settings unavailable"));
    const user = userEvent.setup();
    const { router } = renderApp("/settings");
    await screen.findByText("Settings unavailable");

    await user.click(screen.getByRole("link", { name: "Changelog" }));

    expect(await screen.findByRole("heading", { name: "Changelog", level: 1 })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/changelog");
  });

  it("opens from the Options menu alongside the application log", async () => {
    const user = userEvent.setup();
    const { router } = renderApp("/settings");
    await screen.findByRole("heading", { name: "Settings", level: 1 });

    await user.click(screen.getByRole("button", { name: "Options" }));
    expect(await screen.findByRole("menuitem", { name: "Application Log" })).toBeInTheDocument();
    await user.click(screen.getByRole("menuitem", { name: "Changelog" }));

    expect(await screen.findByRole("heading", { name: "Changelog", level: 1 })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/changelog");
  });
});
