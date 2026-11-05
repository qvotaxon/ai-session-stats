import { Link, Outlet } from "@tanstack/react-router";
import { Activity, Settings } from "lucide-react";
import { DEFAULT_SESSIONS_SEARCH } from "@/features/sessions/searchParams";
import { ThemeToggle } from "@/common/theme/ThemeToggle";

export const AppLayout = () => (
  <div className="min-h-screen">
    <header className="sticky top-0 z-30 border-b bg-background/90 backdrop-blur">
      <div className="mx-auto flex h-14 max-w-7xl items-center justify-between px-4">
        <Link
          to="/"
          search={DEFAULT_SESSIONS_SEARCH}
          className="flex items-center gap-2 font-semibold focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        >
          <Activity className="size-5 text-primary" aria-hidden />
          Session Insights
        </Link>
        <div className="flex items-center gap-1">
          <Link
            to="/settings"
            aria-label="Settings"
            className="inline-flex size-9 items-center justify-center rounded-md text-muted-foreground hover:bg-accent hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
          >
            <Settings className="size-4" aria-hidden />
          </Link>
          <ThemeToggle />
        </div>
      </div>
    </header>
    <main className="mx-auto max-w-7xl px-4 py-6">
      <Outlet />
    </main>
  </div>
);
