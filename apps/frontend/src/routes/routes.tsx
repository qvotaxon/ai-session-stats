import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  lazyRouteComponent,
  type RouterHistory,
} from "@tanstack/react-router";
import { AppLayout } from "@/components/layout/AppLayout";
import { NotFound } from "@/components/layout/NotFound";
import { sessionsSearchSchema } from "@/features/sessions/searchParams";

const rootRoute = createRootRoute({
  component: AppLayout,
  notFoundComponent: () => <NotFound />,
});

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  validateSearch: sessionsSearchSchema,
  component: lazyRouteComponent(() => import("../pages/SessionsPage")),
});

const sessionDetailRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/sessions/$sessionId",
  component: lazyRouteComponent(() => import("../pages/SessionDetailPage")),
});

const settingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/settings",
  component: lazyRouteComponent(() => import("../pages/SettingsPage")),
});

const applicationLogRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/application-log",
  component: lazyRouteComponent(() => import("../pages/ApplicationLogPage")),
});

const routeTree = rootRoute.addChildren([indexRoute, sessionDetailRoute, settingsRoute, applicationLogRoute]);

export const createAppRouter = (history?: RouterHistory) => createRouter({ routeTree, history });

declare module "@tanstack/react-router" {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}

export const router = createAppRouter();

/** Router bound to an in-memory history, for tests. */
export const createTestRouter = (initialPath: string) =>
  createAppRouter(createMemoryHistory({ initialEntries: [initialPath] }));
