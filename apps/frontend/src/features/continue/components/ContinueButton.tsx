import { ChevronDown, Terminal } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { useSetTerminal, useSettings } from "@/features/settings/hooks/useSettings";
import { useContinueSession } from "../hooks/useContinueSession";

interface ContinueButtonProps {
  sessionId: string;
  canContinue: boolean;
}

export const ContinueButton = ({ sessionId, canContinue }: ContinueButtonProps) => {
  const { continueSession, forceContinue, isPending, confirmOpen, setConfirmOpen } = useContinueSession(sessionId);
  const settings = useSettings();
  const setTerminal = useSetTerminal();

  const button = (
    <div className="inline-flex rounded-md shadow-xs">
      <Button
        className="rounded-r-none"
        disabled={!canContinue || isPending || setTerminal.isPending}
        onClick={continueSession}
      >
        <Terminal aria-hidden /> Continue in CLI
      </Button>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button
            size="icon"
            className="rounded-l-none border-l border-primary-foreground/30"
            aria-label="Choose terminal"
            disabled={!settings.data || setTerminal.isPending}
          >
            <ChevronDown aria-hidden />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuLabel>Open in</DropdownMenuLabel>
          <DropdownMenuSeparator />
          <DropdownMenuRadioGroup value={settings.data?.terminal} onValueChange={(value) => setTerminal.mutate(value)}>
            {settings.data?.terminals.map((option) => (
              <DropdownMenuRadioItem
                key={option.id}
                value={option.id}
                disabled={!option.available || setTerminal.isPending}
              >
                {option.label}
              </DropdownMenuRadioItem>
            ))}
          </DropdownMenuRadioGroup>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  );

  return (
    <>
      {canContinue ? (
        button
      ) : (
        <Tooltip>
          <TooltipTrigger asChild>
            <span tabIndex={0}>{button}</span>
          </TooltipTrigger>
          <TooltipContent>The session's working folder no longer exists.</TooltipContent>
        </Tooltip>
      )}

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>This session looks active</AlertDialogTitle>
            <AlertDialogDescription>
              It had activity in the last few minutes and may still be open in another terminal. Resuming it twice can
              mix up the history.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={forceContinue}>Continue anyway</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
};
