import { describe, expect, it } from "vitest";
import {
  formatCompact,
  formatDateTime,
  formatDuration,
  formatDurationMs,
  formatInteger,
  formatNanoAiu,
  formatPercent,
  formatRelative,
} from "./format";

describe("format", () => {
  it("compacts token counts", () => {
    expect(formatCompact(950)).toBe("950");
    expect(formatCompact(1_234_567)).toBe("1.2M");
    expect(formatCompact(12_300)).toBe("12.3K");
  });

  it("converts nano AIU to AIU with a unit", () => {
    expect(formatNanoAiu(2_143_600_000_000)).toBe("2,143.6 AIU");
    expect(formatNanoAiu(1_500_000_000_000_000)).toBe("1.5M AIU");
    expect(formatNanoAiu(0)).toBe("0 AIU");
  });

  it("groups integers", () => {
    expect(formatInteger(1234567)).toBe("1,234,567");
  });

  it("formats percentages", () => {
    expect(formatPercent(0.125)).toBe("12.5%");
    expect(formatPercent(0)).toBe("0%");
  });

  it.each([
    [192, "3m 12s"],
    [45, "45s"],
    [180, "3m"],
    [3700, "1h 1m"],
    [7200, "2h"],
    [0, "0s"],
    [0.2, "<1s"],
  ])("formats %s seconds as %s", (input, expected) => {
    expect(formatDuration(input)).toBe(expected);
  });

  it("shows a dash for missing durations", () => {
    expect(formatDuration(null)).toBe("–");
    expect(formatDurationMs(undefined)).toBe("–");
  });

  it("formats milliseconds below and above one second", () => {
    expect(formatDurationMs(850)).toBe("850 ms");
    expect(formatDurationMs(192_000)).toBe("3m 12s");
  });

  it("formats relative dates against an injected now", () => {
    const now = new Date("2026-11-04T12:00:00Z");
    expect(formatRelative("2026-11-04T09:00:00Z", now)).toBe("3 hours ago");
    expect(formatRelative("2026-11-03T12:00:00Z", now)).toBe("yesterday");
    expect(formatRelative("2026-11-04T11:59:50Z", now)).toBe("just now");
  });

  it("handles missing and invalid dates", () => {
    expect(formatDateTime(null)).toBe("–");
    expect(formatDateTime("nope")).toBe("–");
    expect(formatRelative("nope")).toBe("–");
    expect(formatDateTime("2026-11-04T09:00:00Z")).toContain("2026");
  });
});
