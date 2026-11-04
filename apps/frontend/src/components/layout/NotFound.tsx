import { Link } from "@tanstack/react-router";
import { SearchX } from "lucide-react";
import { DEFAULT_SESSIONS_SEARCH } from "@/features/sessions/searchParams";
import { Button } from "@/components/ui/button";

export const NotFound = ({ title = "Page not found", message }: { title?: string; message?: string }) => (
  <div className="flex flex-col items-center gap-3 py-20 text-center">
    <SearchX className="size-10 text-muted-foreground" aria-hidden />
    <h1 className="text-2xl font-semibold">{title}</h1>
    <p className="max-w-md text-sm text-muted-foreground">
      {message ?? "We could not find what you were looking for."}
    </p>
    <Button asChild>
      <Link to="/" search={DEFAULT_SESSIONS_SEARCH}>
        Back to sessions
      </Link>
    </Button>
  </div>
);
