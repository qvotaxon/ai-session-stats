// Synthetic Session Insights API for visual verification only (no dependencies).
// Usage: node scripts/mock-api.mjs [port]   (default 5181; point the Vite proxy at it with SI_API_URL)
import { createServer } from "node:http";

const port = Number(process.argv[2] ?? process.env.MOCK_PORT ?? 5181);
const MODELS = ["claude-sonnet-4.5", "gpt-5", "claude-opus-4.1"];
const now = Date.now();

const sessions = Array.from({ length: 57 }, (_, i) => {
    const source = i % 3 === 0 ? "claude" : "copilot";
    const startedAt = new Date(now - i * 5_400_000).toISOString();
    const durationSeconds = 120 + ((i * 397) % 7000);
    return {
        id: `${source}-${String(1000 + i)}-aaaa-bbbb`,
        source,
        title: `Refactor module ${i + 1}: improve the retry logic and add tests for the new scheduler`,
        project: i % 5 === 0 ? null : `C:\\git\\project-${i % 4}`,
        startedAt,
        lastActivityAt: startedAt,
        endedAt: new Date(Date.parse(startedAt) + durationSeconds * 1000).toISOString(),
        durationSeconds,
        models: MODELS.slice(0, 1 + (i % 3)),
        totalTokens: 20_000 + ((i * 87_311) % 4_000_000),
        toolCallCount: 5 + ((i * 13) % 200),
        hookCount: (i * 3) % 20,
        guardCount: i % 7,
        subagentCount: i % 4,
        latestReportStatus: null,
    };
});

const detail = (s) => {
    const total = s.totalTokens;
    return {
        ...s,
        tokens: {
            input: Math.round(total * 0.3),
            output: Math.round(total * 0.1),
            cacheRead: Math.round(total * 0.45),
            cacheWrite: Math.round(total * 0.1),
            reasoning: Math.round(total * 0.05),
            total,
        },
        tokensByModel: s.models.map((model, idx) => ({
            model,
            requests: 10 + idx * 4,
            input: 100_000 * (idx + 1),
            output: 30_000 * (idx + 1),
            cacheRead: 200_000,
            cacheWrite: 40_000,
            reasoning: idx === 0 ? 0 : 12_000,
        })),
        toolCalls: {
            total: s.toolCallCount,
            succeeded: s.toolCallCount - 3,
            failed: 3,
            byTool: ["bash", "view", "edit", "grep", "glob", "task", "web_fetch", "create"].map((name, k) => ({
                name,
                count: 40 - k * 4,
                failed: k % 3 === 0 ? 2 : 0,
                avgDurationMs: 300 + k * 900,
                totalDurationMs: (300 + k * 900) * (40 - k * 4),
            })),
        },
        hooks: {
            total: s.hookCount,
            failed: 1,
            byType: [
                { type: "preToolUse", count: 8, failed: 1, avgDurationMs: 120 },
                { type: "postToolUse", count: 6, failed: 0, avgDurationMs: 340 },
                { type: "sessionStart", count: 1, failed: 0, avgDurationMs: 2100 },
            ],
        },
        guards: {
            total: 5,
            blockedHooks: 2,
            permissionDenials: 2,
            hookFailures: 1,
            items: [
                { kind: "blockedHook", name: "no-secrets", count: 2 },
                { kind: "permissionDenial", name: "bash", count: 2 },
                { kind: "hookFailure", name: "preToolUse", count: 1 },
            ],
        },
        subagents: {
            total: 3,
            byType: [
                { name: "explore", count: 2 },
                { name: "task", count: 1 },
            ],
        },
        skills: [{ name: "msdc-frontend", count: 2 }],
        turns: { user: 8, assistant: 31 },
        api: { requestCount: 42, totalApiDurationMs: 183_000 },
        cost: s.source === "copilot" ? { premiumRequests: 12.5, nanoAiu: 4_200_000 } : null,
        codeChanges: s.source === "copilot" ? { linesAdded: 320, linesRemoved: 85, filesModified: 9 } : null,
        timeline: Array.from({ length: 40 }, (_, b) => ({
            bucketStart: new Date(Date.parse(s.startedAt) + b * 90_000).toISOString(),
            toolCalls: Math.round(5 + 4 * Math.sin(b / 3) + (b % 5)),
            hooks: b % 4,
            guards: b % 9 === 0 ? 1 : 0,
            tokens: 20_000 + ((b * 7919) % 60_000),
        })),
        notes:
            s.source === "claude"
                ? [
                      "Premium request cost is not available for this CLI.",
                      "Code change totals are not available for this CLI.",
                  ]
                : [],
    };
};

/** @type {Map<string, any[]>} */
const runs = new Map();
const REPORT_MD = `# Session report

A **synthetic** report with a table:

| Metric | Value |
| --- | --- |
| Tokens | 1.2M |
| Tools | 87 |

## Highlights

- Fast iteration
- Two guard blocks

<script>alert("raw html must not render")</script>

\`\`\`ts
const x = 1;
\`\`\`
`;

const advance = (run) => {
    const age = Date.now() - Date.parse(run.startedAt);
    if (run.status === "cancelled") return;
    if (age > 6000) {
        run.status = "succeeded";
        run.finishedAt ??= new Date().toISOString();
        run.durationMs ??= age;
        run.exitCode = 0;
    } else if (age > 2000) run.status = "running";
};

const send = (res, status, body, type = "application/json") => {
    res.writeHead(status, { "content-type": type });
    res.end(typeof body === "string" ? body : JSON.stringify(body));
};
const problem = (res, status, title) => send(res, status, { title, status }, "application/problem+json");

createServer((req, res) => {
    const url = new URL(req.url, "http://localhost");
    const parts = url.pathname.split("/").filter(Boolean);
    if (url.pathname === "/health") return send(res, 200, { status: "Healthy" });
    if (url.pathname === "/api/v1/overview") {
        return send(res, 200, {
            generatedAt: new Date().toISOString(),
            totalSessions: sessions.length,
            recentlyActiveSessions: sessions.filter((s) => Date.parse(s.lastActivityAt) >= Date.now() - 7 * 86400000).length,
            totalTokens: sessions.reduce((n, s) => n + s.totalTokens, 0),
            toolCalls: sessions.reduce((n, s) => n + s.toolCallCount, 0),
            toolFailures: sessions.length * 3,
            hooks: sessions.reduce((n, s) => n + s.hookCount, 0),
            hookFailures: sessions.length,
            sessionsWithVersionEvidence: 0,
            attributedHealthEvents: 0,
            totalHealthEvents: sessions.reduce((n, s) => n + s.toolCallCount + s.hookCount, 0),
            recentSessions: sessions.slice(0, 3),
            harnessVersions: [],
        });
    }
    if (url.pathname === "/api/v1/harness/installations") {
        return send(res, 200, ["claude", "copilot"].map((source) => ({
            source,
            status: "unavailable",
            checkedAt: new Date().toISOString(),
            installations: [],
            error: "Synthetic API: local CLI inventory is not available.",
        })));
    }
    if (parts[0] !== "api" || parts[1] !== "v1" || parts[2] !== "sessions") return problem(res, 404, "Not found");

    if (parts.length === 3) {
        const q = url.searchParams;
        const page = Number(q.get("page") ?? 1);
        const pageSize = Math.min(Number(q.get("pageSize") ?? 25), 100);
        const sort = q.get("sort") ?? "startedAt";
        const dir = q.get("direction") === "asc" ? 1 : -1;
        const search = (q.get("search") ?? "").toLowerCase();
        let list = sessions.filter((s) => !q.get("source") || s.source === q.get("source"));
        if (search) list = list.filter((s) => `${s.title} ${s.project} ${s.id}`.toLowerCase().includes(search));
        list = [...list].sort((a, b) => (a[sort] > b[sort] ? 1 : a[sort] < b[sort] ? -1 : 0) * dir);
        const items = list.slice((page - 1) * pageSize, page * pageSize).map((s) => {
            const r = runs.get(s.id);
            return { ...s, latestReportStatus: r?.[0]?.status ?? null };
        });
        return send(res, 200, {
            items,
            page,
            pageSize,
            totalCount: list.length,
            totalPages: Math.ceil(list.length / pageSize),
        });
    }

    const session = sessions.find((s) => s.id === decodeURIComponent(parts[3]));
    if (!session) return problem(res, 404, "Session not found");
    if (parts.length === 4) return send(res, 200, detail(session));

    if (parts[4] !== "reports") return problem(res, 404, "Not found");
    const list = runs.get(session.id) ?? [];
    list.forEach(advance);
    if (parts.length === 5 && req.method === "GET") return send(res, 200, list);
    if (parts.length === 5 && req.method === "POST") {
        if (list.some((r) => r.status === "queued" || r.status === "running"))
            return problem(res, 409, "A report run is already active");
        const run = {
            id: `run-${list.length + 1}`,
            sessionId: session.id,
            version: list.length + 1,
            status: "queued",
            cli: session.source,
            forked: session.source === "claude",
            forkedSessionId: null,
            startedAt: new Date().toISOString(),
            finishedAt: null,
            durationMs: null,
            exitCode: null,
            error: null,
        };
        runs.set(session.id, [run, ...list]);
        return send(res, 202, run);
    }
    const run = list.find((r) => r.id === parts[5]);
    if (!run) return problem(res, 404, "Run not found");
    if (parts[6] === "cancel" && req.method === "POST") {
        if (run.status !== "running") return problem(res, 409, "Run is not running");
        run.status = "cancelled";
        run.finishedAt = new Date().toISOString();
        return send(res, 202, run);
    }
    if (parts[6] === "content" || parts[6] === "download") {
        if (run.status !== "succeeded") return problem(res, 404, "Report not ready");
        res.writeHead(200, {
            "content-type": "text/markdown",
            ...(parts[6] === "download"
                ? { "content-disposition": `attachment; filename="session-${session.id}-v${run.version}.md"` }
                : {}),
        });
        return res.end(REPORT_MD);
    }
    return send(res, 200, run);
}).listen(port, "127.0.0.1", () => console.log(`mock api on http://127.0.0.1:${port}`));
