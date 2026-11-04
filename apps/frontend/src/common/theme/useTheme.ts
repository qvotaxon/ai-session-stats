import { useContext } from "react";
import { ThemeContext, type ThemeContextValue } from "./themeContext";

export const useTheme = (): ThemeContextValue => {
  const context = useContext(ThemeContext);
  // Outside a provider (isolated component tests, error fallbacks) behave as "system" without persistence.
  return context ?? { theme: "system", setTheme: () => undefined };
};
