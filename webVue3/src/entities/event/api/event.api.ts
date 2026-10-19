import { apiClient, unwrap, withFlags, type ApiSchema } from '@/shared/api';

export type EventAccepted = ApiSchema<'EventAcceptedResponse'>;
/** `status` is `pending` | `processed` | `failed` in practice; the API sends a plain string. */
export type EventStatus = ApiSchema<'EventStatusResponse'>;

/**
 * The admin-JWT path to the same publish pipeline as the API-key `/generic` route, for testing
 * from the portal (EventsModule.cs `simulate`). The caller shows the error itself.
 */
export function simulateEvent(
  tenantId: string,
  eventType: string,
  data: Record<string, unknown>,
): Promise<EventAccepted> {
  return unwrap(
    apiClient().POST('/api/v1/tenants/{tenantId}/events/simulate', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId } },
      body: { eventType, data },
    }),
  );
}

/** Polled after a send; a failure just leaves the last status shown, so it doesn't toast. */
export function getEventStatus(tenantId: string, eventId: string): Promise<EventStatus> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/events/{eventId}', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId, eventId } },
    }),
  );
}
