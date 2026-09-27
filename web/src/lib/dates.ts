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
