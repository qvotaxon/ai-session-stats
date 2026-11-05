import { z } from "zod";

export const terminalOptionSchema = z.object({
  id: z.string(),
  label: z.string(),
  available: z.boolean(),
});

export const settingsSchema = z.object({
  terminal: z.string(),
  terminals: z.array(terminalOptionSchema),
});

export type TerminalOption = z.infer<typeof terminalOptionSchema>;
export type Settings = z.infer<typeof settingsSchema>;
