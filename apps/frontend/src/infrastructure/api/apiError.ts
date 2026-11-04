import { isAxiosError } from "axios";
import { z } from "zod";

const problemDetailsSchema = z.object({
  type: z.string().optional(),
  title: z.string().optional(),
  status: z.number().optional(),
  detail: z.string().optional(),
});

/** Typed error produced by the response interceptor for every failed API call (RFC 9457 ProblemDetails aware). */
export class ApiError extends Error {
  readonly status: number | null;
  readonly title: string;
  readonly detail: string | null;

  constructor(message: string, status: number | null, title: string, detail: string | null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.title = title;
    this.detail = detail;
  }
}

export const toApiError = (error: unknown): ApiError => {
  if (error instanceof ApiError) return error;
  if (isAxiosError(error)) {
    const status = error.response?.status ?? null;
    const parsed = problemDetailsSchema.safeParse(error.response?.data);
    const problem = parsed.success ? parsed.data : undefined;
    const title = problem?.title ?? (status === null ? "Cannot reach the Session Insights service" : "Request failed");
    const detail = problem?.detail ?? null;
    return new ApiError(detail ?? title, status, title, detail);
  }
  return new ApiError(error instanceof Error ? error.message : "Unexpected error", null, "Unexpected error", null);
};

export const isApiError = (error: unknown, status?: number): error is ApiError =>
  error instanceof ApiError && (status === undefined || error.status === status);
