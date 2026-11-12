import { Link } from "@tanstack/react-router";
import { ArrowLeft } from "lucide-react";
import changelog from "../../../../CHANGELOG.md?raw";
import { MarkdownView } from "@/common/MarkdownView";
import { Button } from "@/components/ui/button";

const ChangelogPage = () => (
  <div className="space-y-4">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <h1 className="text-2xl font-semibold tracking-tight">Changelog</h1>
      <Button variant="outline" asChild>
        <Link to="/settings">
          <ArrowLeft className="size-4" aria-hidden /> Back to settings
        </Link>
      </Button>
    </div>
    <p className="text-sm text-muted-foreground">
      Release history included with this build. Available offline; newer releases appear after updating the app.
    </p>
    <MarkdownView className="rounded-md border p-4 text-sm">
      {changelog.replace(/^# Changelog\r?\n+/, "")}
    </MarkdownView>
  </div>
);

export default ChangelogPage;
