import { Link, Outlet } from "@tanstack/react-router";
import { Activity, FileText, History, Settings } from "lucide-react";
import { DEFAULT_SESSIONS_SEARCH } from "@/features/sessions/searchParams";
import { ThemeToggle } from "@/common/theme/ThemeToggle";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

export const AppLayout = () => (
  <div className="min-h-screen">
    <header className="sticky top-0 z-30 border-b bg-background/90 backdrop-blur">
      <div className="mx-auto flex min-h-14 max-w-7xl flex-wrap items-center justify-between gap-2 px-4 py-2">
        <Link
          to="/"
          className="flex items-center gap-2 font-semibold focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        >
          <Activity className="size-5 text-primary" aria-hidden />
          Session Insights
        </Link>
        <nav
          aria-label="Main navigation"
          className="order-last flex w-full items-center justify-center gap-1 sm:order-none sm:w-auto"
        >
          <Button asChild variant="ghost" size="sm">
            <Link to="/" activeProps={{ className: "bg-accent" }} activeOptions={{ exact: true }}>
              Dashboard
            </Link>
          </Button>
          <Button asChild variant="ghost" size="sm">
            <Link to="/sessions" search={DEFAULT_SESSIONS_SEARCH} activeProps={{ className: "bg-accent" }}>
              Sessions
            </Link>
          </Button>
          <Button asChild variant="ghost" size="sm">
            <Link to="/harness" activeProps={{ className: "bg-accent" }}>
              Harness
            </Link>
          </Button>
        </nav>
        <div className="flex items-center gap-1">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" aria-label="Options">
                <Settings className="size-4" aria-hidden />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem asChild>
                <Link to="/settings">
                  <Settings className="size-4" aria-hidden /> Settings
                </Link>
              </DropdownMenuItem>
              <DropdownMenuItem asChild>
                <Link to="/application-log">
                  <FileText className="size-4" aria-hidden /> Application Log
                </Link>
              </DropdownMenuItem>
              <DropdownMenuItem asChild>
                <Link to="/changelog">
                  <History className="size-4" aria-hidden /> Changelog
                </Link>
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
          <ThemeToggle />
        </div>
      </div>
    </header>
    <main className="mx-auto max-w-7xl px-4 py-6">
      <Outlet />
    </main>
  </div>
);
