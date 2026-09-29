// When something happened, as a day: "Oct 1, 2026" in the reader's time zone.
export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

// A calendar date the API sends without a time, such as a due date. Read in
// UTC, or a reader west of Greenwich would see the day before.
export function formatDueDate(date: string): string {
  return new Date(date).toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
    timeZone: "UTC",
  });
}

// When something happened, to the minute, in the reader's time zone.
export function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });
}

// How long ago something happened, as a person says it: "a moment ago" for
// the first minute, then minutes and hours, then the day it happened.
export function formatAgo(iso: string, now: number): string {
  const minutes = Math.floor((now - new Date(iso).getTime()) / 60_000);
  if (minutes < 1) return "a moment ago";
  if (minutes < 60) return `${minutes} ${minutes === 1 ? "minute" : "minutes"} ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} ${hours === 1 ? "hour" : "hours"} ago`;
  return `on ${formatTimestamp(iso)}`;
}
