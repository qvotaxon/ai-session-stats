import { AxiosError, AxiosHeaders, type InternalAxiosRequestConfig } from "axios";
import { afterEach, describe, expect, it } from "vitest";
import { ApiError, apiClient, toApiError } from "@/infrastructure/api";

describe("api client", () => {
  afterEach(() => {
    document.head.querySelectorAll('meta[name="si-token"]').forEach((m) => m.remove());
  });

  it("attaches the token header from the meta tag", async () => {
    document.head.insertAdjacentHTML("beforeend", '<meta name="si-token" content="abc123">');
    let seen: string | undefined;
    await apiClient.get("/ping", {
      adapter: (config) => {
        seen = config.headers.get("X-Session-Insights-Token")?.toString();
        return Promise.resolve({ data: {}, status: 200, statusText: "OK", headers: {}, config });
      },
    });
    expect(seen).toBe("abc123");
  });

  it("omits the header when no meta tag is present", async () => {
    let seen: unknown = "unset";
    await apiClient.get("/ping", {
      adapter: (config) => {
        seen = config.headers.get("X-Session-Insights-Token");
        return Promise.resolve({ data: {}, status: 200, statusText: "OK", headers: {}, config });
      },
    });
    expect(seen).toBeFalsy();
  });

  it("maps ProblemDetails responses to ApiError", async () => {
    const error = await apiClient
      .get("/ping", {
        adapter: (config) =>
          Promise.reject(
            new AxiosError("Request failed", "ERR_BAD_REQUEST", config as InternalAxiosRequestConfig, null, {
              status: 409,
              statusText: "Conflict",
              headers: {},
              config: { headers: new AxiosHeaders() },
              data: { title: "Conflict", detail: "A run is already active", status: 409 },
            }),
          ),
      })
      .catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, title: "Conflict", detail: "A run is already active" });
  });

  it("maps network failures to an ApiError without a status", () => {
    const mapped = toApiError(new AxiosError("Network Error", "ERR_NETWORK"));
    expect(mapped.status).toBeNull();
    expect(mapped.title).toMatch(/Cannot reach/);
  });
});
