import { AlertCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";

const SomethingWentWrong: React.FC = () => {
  return (
    <div className="flex h-screen items-center justify-center p-6">
      <Card className="max-w-md text-center">
        <CardContent className="space-y-6 p-6">
          <div className="flex flex-col items-center space-y-3">
            <AlertCircle className="h-12 w-12 text-destructive" />
            <h2 className="text-xl font-semibold">Something went wrong!</h2>
            <p className="text-sm text-muted-foreground">
              An unexpected error occurred. Please try reloading the page.
            </p>
          </div>

          <Button onClick={() => window.location.reload()}>Reload Page</Button>
        </CardContent>
      </Card>
    </div>
  );
};

export default SomethingWentWrong;
