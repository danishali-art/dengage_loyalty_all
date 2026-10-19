import { apiClient, unwrap, withFlags, type ApiSchema } from '@/shared/api';

/**
 * Every event type this deployment accepts: the built-ins plus the generic types configured in
 * `RabbitMq:GenericEventTypes` (deployment-wide). Rule and streak builders offer `builtIn` +
 * `generic`.
 */
export type EventTypesCatalog = ApiSchema<'EventTypesResponse'>;

/** The trigger pickers fall back to the built-in list when this fails, so it doesn't toast. */
export function getEventTypes(tenantId: string): Promise<EventTypesCatalog> {
  return unwrap(
    apiClient().GET('/api/v1/tenants/{tenantId}/events/types', {
      ...withFlags({ skipErrorToast: true }),
      params: { path: { tenantId } },
    }),
  );
}
