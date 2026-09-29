export interface EventAccepted {
  eventId: string;
  status: string;
}

/** `pending` | `processed` | `failed`, in practice — the backend returns a plain string. */
export interface EventStatus {
  eventId: string;
  eventType: string;
  status: string;
  receivedAt: string;
  processedAt: string | null;
  error: string | null;
}
