import { fireEvent, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { renderApp } from "@/common/testSetup/renderHelpers";
import { evolveApi } from "@/features/evolve/evolveApi";
import { sessionsApi } from "@/features/sessions/api/sessionsApi";

vi.mock("@/features/evolve/evolveApi", () => ({ evolveApi: { projects: vi.fn(), analyze: vi.fn() } }));
vi.mock("@/features/sessions/api/sessionsApi", () => ({ sessionsApi: { list: vi.fn() } }));

const sessionId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
const projectId = "a".repeat(64);

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(evolveApi.projects).mockResolvedValue([{ id: projectId, name: "Demo", sessionCount: 2 }]);
  vi.mocked(sessionsApi.list).mockResolvedValue({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 });
});

it("analyzes a project with an open-ended local time range and links to supporting sessions", async () => {
  const user = userEvent.setup();
  vi.mocked(evolveApi.analyze).mockResolvedValue({
    sessionsScanned: 2,
    requestsAnalyzed: 2,
    sampled: false,
    opportunities: [
      {
        id: "run-tests",
        title: "Run and summarize tests",
        outline: "Capture how to run tests.",
        instructions: "# Run tests",
        occurrences: 2,
        sessionCount: 1,
        evidence: [{ sessionId, title: "Test session", occurrences: 2 }],
      },
    ],
  });
  renderApp("/evolve");
  await screen.findByRole("option", { name: "Demo · 2 sessions" });
  await user.selectOptions(screen.getByLabelText("Project"), projectId);
  const localStart = "2026-03-01T10:30";
  fireEvent.change(screen.getByLabelText("From (optional)"), { target: { value: localStart } });
  await user.click(screen.getByRole("checkbox"));
  await user.click(screen.getByRole("button", { name: "Analyze" }));

  expect(await screen.findByRole("link", { name: "Test session" })).toHaveAttribute("href", `/sessions/${sessionId}`);
  expect(vi.mocked(evolveApi.analyze).mock.calls[0]?.[0]).toEqual({
    projectId,
    from: new Date(localStart).toISOString(),
    to: undefined,
    cli: "copilot",
    consent: true,
  });
});

it("uses the linked session and prevents submission of an inverted range", async () => {
  const user = userEvent.setup();
  vi.mocked(evolveApi.analyze).mockResolvedValue({
    sessionsScanned: 1,
    requestsAnalyzed: 0,
    sampled: false,
    opportunities: [],
  });
  renderApp(`/evolve?sessionId=${sessionId}`);
  await screen.findByRole("heading", { name: "Evolve" });
  expect(screen.getByLabelText("Scope")).toHaveValue("session");
  const from = screen.getByLabelText("From (optional)");
  fireEvent.change(from, { target: { value: "2026-03-02T10:30" } });
  fireEvent.change(screen.getByLabelText("To (optional)"), { target: { value: "2026-03-01T10:30" } });
  expect(screen.getByRole("alert")).toHaveTextContent("The start time must be on or before the end time.");
  expect(screen.getByRole("button", { name: "Analyze" })).toBeDisabled();
  expect(evolveApi.analyze).not.toHaveBeenCalled();

  fireEvent.change(from, { target: { value: "" } });
  await user.click(screen.getByRole("checkbox"));
  await user.click(screen.getByRole("button", { name: "Analyze" }));
  expect(await screen.findByText(/No repeated task patterns were strong enough/)).toBeInTheDocument();
  expect(vi.mocked(evolveApi.analyze).mock.calls[0]?.[0]).toMatchObject({ sessionId });
});

it("clears consent and previous results when the selected CLI changes", async () => {
  const user = userEvent.setup();
  vi.mocked(evolveApi.analyze).mockResolvedValue({
    sessionsScanned: 2,
    requestsAnalyzed: 2,
    sampled: false,
    opportunities: [],
  });
  renderApp(`/evolve?sessionId=${sessionId}`);
  await screen.findByRole("heading", { name: "Evolve" });
  await user.click(screen.getByRole("checkbox"));
  await user.click(screen.getByRole("button", { name: "Analyze" }));
  await screen.findByText(/Analyzed 2 requests/);

  await user.selectOptions(screen.getByLabelText("Local CLI"), "claude");

  expect(screen.getByRole("checkbox")).not.toBeChecked();
  expect(screen.queryByText(/Analyzed 2 requests/)).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Analyze" })).toBeDisabled();
});

it("does not claim the analysis is local or model-free", async () => {
  renderApp("/evolve");
  await screen.findByRole("heading", { name: "Evolve" });
  expect(screen.getByText("Find recurring requests that could become reusable skills.")).toBeInTheDocument();
  expect(screen.queryByText(/uses no AI models/i)).not.toBeInTheDocument();
  expect(screen.queryByText(/stays on this device/i)).not.toBeInTheDocument();
});

it("says the CLI's configured provider receives the requests, for both CLIs", async () => {
  const user = userEvent.setup();
  renderApp("/evolve");
  await screen.findByRole("heading", { name: "Evolve" });
  expect(screen.getByText(/By default it sends them to GitHub's Copilot service/)).toBeInTheDocument();

  await user.selectOptions(screen.getByLabelText("Local CLI"), "claude");

  expect(screen.getByText(/By default it sends them to Anthropic's service/)).toBeInTheDocument();
  expect(screen.queryByText(/GitHub's Copilot service/)).not.toBeInTheDocument();
  expect(screen.getByText(/configure it to use another AI provider/)).toBeInTheDocument();
  expect(screen.queryByText(/only GitHub/i)).not.toBeInTheDocument();
  expect(screen.queryByText(/never leaves your device/i)).not.toBeInTheDocument();
});
