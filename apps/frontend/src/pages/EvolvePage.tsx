import { useMutation, useQuery } from "@tanstack/react-query";
import { Link, useSearch } from "@tanstack/react-router";
import { Lightbulb } from "lucide-react";
import { useState, type FormEvent } from "react";
import { QueryErrorAlert } from "@/common/QueryErrorAlert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { evolveApi } from "@/features/evolve/evolveApi";
import { PaginationBar } from "@/features/sessions/components/PaginationBar";
import { useSessionsList } from "@/features/sessions/hooks/useSessions";

const selectClass = "h-9 w-full rounded-md border border-input bg-background px-3 text-sm shadow-xs";

const SessionPicker = ({ id, onChange }: { id: string; onChange: (id: string) => void }) => {
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const query = useSessionsList({ page, pageSize: 10, search, sort: "lastActivityAt", direction: "desc" });

  return (
    <div className="space-y-2">
      <label className="block space-y-1 text-sm font-medium">
        Search sessions
        <Input
          placeholder="Title, project, or session ID"
          value={search}
          onChange={(event) => {
            setSearch(event.target.value);
            setPage(1);
          }}
        />
      </label>
      <label className="block space-y-1 text-sm font-medium">
        Session
        <select className={selectClass} value={id} onChange={(event) => onChange(event.target.value)}>
          <option value="">Select a session</option>
          {id && !query.data?.items.some((session) => session.id === id) && <option value={id}>{id}</option>}
          {query.data?.items.map((session) => (
            <option key={session.id} value={session.id}>
              {session.title || session.id} · {session.source} · {new Date(session.startedAt).toLocaleDateString()}
            </option>
          ))}
        </select>
      </label>
      {query.isPending && (
        <p role="status" className="text-sm text-muted-foreground">
          Loading sessions…
        </p>
      )}
      {query.isError && (
        <QueryErrorAlert title="Could not load sessions" error={query.error} onRetry={() => void query.refetch()} />
      )}
      {query.data && !query.isError && <PaginationBar {...query.data} onPageChange={setPage} />}
    </div>
  );
};

const ProjectPicker = ({ id, onChange }: { id: string; onChange: (id: string) => void }) => {
  const query = useQuery({ queryKey: ["evolve", "projects"], queryFn: ({ signal }) => evolveApi.projects(signal) });
  const names = new Map<string, number>();
  for (const project of query.data ?? []) {
    const name = project.name.toLowerCase();
    names.set(name, (names.get(name) ?? 0) + 1);
  }
  return (
    <div className="space-y-2">
      <label className="block space-y-1 text-sm font-medium">
        Project
        <select className={selectClass} value={id} onChange={(event) => onChange(event.target.value)}>
          <option value="">Select a project</option>
          {query.data?.map((project) => (
            <option key={project.id} value={project.id}>
              {project.name}
              {(names.get(project.name.toLowerCase()) ?? 0) > 1 ? ` (${project.id.slice(0, 8)})` : ""}
              {` · ${project.sessionCount} ${project.sessionCount === 1 ? "session" : "sessions"}`}
            </option>
          ))}
        </select>
      </label>
      {query.isPending && (
        <p role="status" className="text-sm text-muted-foreground">
          Loading projects…
        </p>
      )}
      {query.isError && (
        <QueryErrorAlert title="Could not load projects" error={query.error} onRetry={() => void query.refetch()} />
      )}
      {query.data?.length === 0 && (
        <p className="text-sm text-muted-foreground">No sessions have a recorded project folder.</p>
      )}
    </div>
  );
};

const EvolvePage = () => {
  const { sessionId: initialSession } = useSearch({ from: "/evolve" });
  return <EvolveForm key={initialSession} initialSession={initialSession} />;
};

const EvolveForm = ({ initialSession }: { initialSession?: string }) => {
  const [scope, setScope] = useState<"session" | "project">(initialSession ? "session" : "project");
  const [sessionId, setSessionId] = useState(initialSession ?? "");
  const [projectId, setProjectId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const analysis = useMutation({ mutationFn: evolveApi.analyze });
  const start = from ? new Date(from) : undefined;
  const end = to ? new Date(to) : undefined;
  const invalidRange = start && end && start > end;
  const scopeId = scope === "session" ? sessionId : projectId;

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!scopeId || invalidRange || analysis.isPending) return;
    analysis.mutate({
      ...(scope === "session" ? { sessionId } : { projectId }),
      from: start?.toISOString(),
      to: end?.toISOString(),
    });
  }

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">Evolve</h1>
        <p className="text-sm text-muted-foreground">
          Find recurring requests that could become reusable skills. Analysis stays on this device and uses no AI
          models.
        </p>
      </header>
      <Card>
        <CardHeader>
          <CardTitle>Find skill opportunities</CardTitle>
          <CardDescription>
            Choose one session or all Copilot and Claude sessions recorded in a project folder.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={submit}>
            <fieldset disabled={analysis.isPending} className="space-y-4">
              <label className="block space-y-1 text-sm font-medium">
                Scope
                <select
                  className={selectClass}
                  value={scope}
                  onChange={(event) => {
                    setScope(event.target.value === "session" ? "session" : "project");
                    analysis.reset();
                  }}
                >
                  <option value="project">Project</option>
                  <option value="session">Single session</option>
                </select>
              </label>
              {scope === "session" ? (
                <SessionPicker
                  id={sessionId}
                  onChange={(id) => {
                    setSessionId(id);
                    analysis.reset();
                  }}
                />
              ) : (
                <ProjectPicker
                  id={projectId}
                  onChange={(id) => {
                    setProjectId(id);
                    analysis.reset();
                  }}
                />
              )}
              <div className="grid gap-3 sm:grid-cols-2">
                <label className="space-y-1 text-sm font-medium">
                  From (optional)
                  <Input
                    type="datetime-local"
                    value={from}
                    onChange={(event) => {
                      setFrom(event.target.value);
                      analysis.reset();
                    }}
                  />
                </label>
                <label className="space-y-1 text-sm font-medium">
                  To (optional)
                  <Input
                    type="datetime-local"
                    value={to}
                    onChange={(event) => {
                      setTo(event.target.value);
                      analysis.reset();
                    }}
                  />
                </label>
              </div>
              <p className="text-sm text-muted-foreground">
                Times are local and filter individual requests, not session start dates.
              </p>
              {invalidRange && (
                <p role="alert" className="text-sm text-destructive">
                  The start time must be on or before the end time.
                </p>
              )}
              <Button type="submit" disabled={!scopeId || invalidRange || analysis.isPending}>
                <Lightbulb className="size-4" aria-hidden /> {analysis.isPending ? "Analyzing…" : "Analyze"}
              </Button>
            </fieldset>
          </form>
        </CardContent>
      </Card>
      {analysis.isError && <QueryErrorAlert title="Could not analyze skill opportunities" error={analysis.error} />}
      {analysis.data && (
        <section className="space-y-3" aria-labelledby="evolve-results">
          <h2 id="evolve-results" className="text-lg font-semibold">
            Skill opportunities
          </h2>
          <p role="status" className="text-sm text-muted-foreground">
            {analysis.data.sessionsScanned} {analysis.data.sessionsScanned === 1 ? "session" : "sessions"} checked.{" "}
            Suggestions are based on recognized workflows requested at least twice.
          </p>
          {analysis.data.opportunities.length === 0 && (
            <p className="rounded-lg border border-dashed p-6 text-sm text-muted-foreground">
              No recurring workflows were found. Try a wider time range or analyze a whole project.
            </p>
          )}
          {analysis.data.opportunities.map((opportunity) => (
            <Card key={opportunity.id}>
              <CardHeader>
                <CardTitle>{opportunity.title}</CardTitle>
                <CardDescription>
                  {opportunity.occurrences} requests across {opportunity.sessionCount}{" "}
                  {opportunity.sessionCount === 1 ? "session" : "sessions"}
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-3 text-sm">
                <p>{opportunity.outline}</p>
                <div>
                  <h3 className="font-medium">Supporting sessions</h3>
                  <ul className="list-disc space-y-1 pl-5">
                    {opportunity.evidence.map((entry) => (
                      <li key={entry.sessionId}>
                        <Link
                          className="break-words underline"
                          to="/sessions/$sessionId"
                          params={{ sessionId: entry.sessionId }}
                        >
                          {entry.title || entry.sessionId}
                        </Link>{" "}
                        ({entry.occurrences})
                      </li>
                    ))}
                  </ul>
                  {opportunity.sessionCount > opportunity.evidence.length && (
                    <p className="mt-1 text-muted-foreground">
                      Showing the top {opportunity.evidence.length} supporting sessions.
                    </p>
                  )}
                </div>
              </CardContent>
            </Card>
          ))}
        </section>
      )}
    </div>
  );
};

export default EvolvePage;
