import { z } from "zod";

// Zod probes `new Function` to enable JIT parsing. The throw is caught, but our strict CSP (no 'unsafe-eval')
// reports it as a violation. Disabling JIT skips the probe. This module must be the first import in main.tsx,
// because schemas read the setting when they are constructed.
z.config({ jitless: true });
