import { Link } from "@tanstack/react-router";
import { History } from "lucide-react";
import { Button } from "@/components/ui/button";
import { SettingsView } from "@/features/settings/components/SettingsView";

const SettingsPage = () => (
  <div className="space-y-6">
    <h1 className="text-2xl font-semibold tracking-tight">Settings</h1>
    <SettingsView />
    <Button variant="outline" asChild>
      <Link to="/changelog">
        <History className="size-4" aria-hidden /> Changelog
      </Link>
    </Button>
  </div>
);

export default SettingsPage;
