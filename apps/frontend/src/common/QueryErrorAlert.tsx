import { AlertCircle } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";

export const QueryErrorAlert = ({ title, error, onRetry }: { title: string; error: Error; onRetry?: () => void }) => (
  <Alert variant="destructive">
    <AlertCircle className="size-4" aria-hidden />
    <AlertTitle>{title}</AlertTitle>
    <AlertDescription>
      <p>{error.message}</p>
      {onRetry && (
        <Button variant="outline" size="sm" className="mt-2" onClick={onRetry}>
          Try again
        </Button>
      )}
    </AlertDescription>
  </Alert>
);
