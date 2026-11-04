import axios from "axios";
import { toApiError } from "./apiError";

const TOKEN_HEADER = "X-Session-Insights-Token";

/** The backend injects the token into index.html in production; the Vite dev proxy adds it in development. */
export const readTokenFromMeta = (): string | null =>
  document.querySelector<HTMLMetaElement>('meta[name="si-token"]')?.content?.trim() || null;

const apiClient = axios.create({
  baseURL: "/api/v1",
  timeout: 30_000,
});

apiClient.interceptors.request.use((config) => {
  const token = readTokenFromMeta();
  if (token) {
    config.headers.set(TOKEN_HEADER, token);
  }
  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  async (error: unknown) => {
    // Blob responses (downloads) hold the ProblemDetails as a Blob: decode it so the mapper can read it.
    if (axios.isAxiosError(error) && error.response?.data instanceof Blob) {
      try {
        error.response.data = JSON.parse(await error.response.data.text());
      } catch {
        error.response.data = undefined;
      }
    }
    return Promise.reject(toApiError(error));
  },
);

export { apiClient };
