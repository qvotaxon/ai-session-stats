const NO_VALUE = "–";

const compactFormat = new Intl.NumberFormat("en", { notation: "compact", maximumFractionDigits: 1 });
const integerFormat = new Intl.NumberFormat("en");

/** 1_234_567 -> "1.2M". */
export const formatCompact = (value: number): string => compactFormat.format(value);

/** 1234567 -> "1,234,567". */
export const formatInteger = (value: number): string => integerFormat.format(value);

const aiuFormat = new Intl.NumberFormat("en", { maximumFractionDigits: 1 });
const NANO_PER_AIU = 1e9;

/** nano AIU -> AIU with a unit: 2_143_600_000_000 -> "2,143.6 AIU"; from 1M AIU upward compact ("1.5M AIU"). */
export const formatNanoAiu = (nanoAiu: number): string => {
  const aiu = nanoAiu / NANO_PER_AIU;
  return `${aiu >= 1_000_000 ? compactFormat.format(aiu) : aiuFormat.format(aiu)} AIU`;
};

export const formatPercent = (fraction: number): string =>
  `${new Intl.NumberFormat("en", { maximumFractionDigits: 1 }).format(fraction * 100)}%`;

/** 192 -> "3m 12s", 45 -> "45s", 3700 -> "1h 1m". Sub-second values read "<1s". */
export const formatDuration = (totalSeconds: number | null | undefined): string => {
  if (totalSeconds === null || totalSeconds === undefined || Number.isNaN(totalSeconds)) return NO_VALUE;
  const seconds = Math.round(totalSeconds);
  if (seconds <= 0) return totalSeconds > 0 ? "<1s" : "0s";
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const rest = seconds % 60;
  if (hours > 0) return minutes > 0 ? `${hours}h ${minutes}m` : `${hours}h`;
  if (minutes > 0) return rest > 0 ? `${minutes}m ${rest}s` : `${minutes}m`;
  return `${rest}s`;
};

/** 850 -> "850 ms", 192_000 -> "3m 12s". */
export const formatDurationMs = (ms: number | null | undefined): string => {
  if (ms === null || ms === undefined || Number.isNaN(ms)) return NO_VALUE;
  if (ms < 1000) return `${Math.round(ms)} ms`;
  return formatDuration(ms / 1000);
};

const absoluteFormat = new Intl.DateTimeFormat("en-GB", { dateStyle: "medium", timeStyle: "short" });
const relativeFormat = new Intl.RelativeTimeFormat("en", { numeric: "auto" });

export const formatDateTime = (iso: string | null | undefined): string => {
  if (!iso) return NO_VALUE;
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? NO_VALUE : absoluteFormat.format(date);
};

const RELATIVE_UNITS: ReadonlyArray<readonly [Intl.RelativeTimeFormatUnit, number]> = [
  ["year", 365 * 24 * 3600],
  ["month", 30 * 24 * 3600],
  ["day", 24 * 3600],
  ["hour", 3600],
  ["minute", 60],
];

/** "3 hours ago", "yesterday". `now` is injectable for tests. */
export const formatRelative = (iso: string | null | undefined, now: Date = new Date()): string => {
  if (!iso) return NO_VALUE;
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return NO_VALUE;
  const diffSeconds = (date.getTime() - now.getTime()) / 1000;
  const abs = Math.abs(diffSeconds);
  for (const [unit, size] of RELATIVE_UNITS) {
    if (abs >= size) return relativeFormat.format(Math.trunc(diffSeconds / size), unit);
  }
  return "just now";
};

export const formatTime = (iso: string): string => {
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? NO_VALUE
    : new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" }).format(date);
};
