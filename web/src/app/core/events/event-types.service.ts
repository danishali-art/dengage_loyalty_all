import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../http/api-client';

/** Every event type this deployment will accept: the 7 built-ins plus whatever generic types
 * are configured in `RabbitMq:GenericEventTypes` (deployment-wide, not tenant-scoped). */
export interface EventTypesCatalog {
  builtIn: readonly string[];
  generic: readonly string[];
}

@Injectable({ providedIn: 'root' })
export class EventTypesService {
  private readonly api = inject(ApiClient);

  get(): Promise<EventTypesCatalog> {
    return firstValueFrom(this.api.tenantScope.get<EventTypesCatalog>('events/types'));
  }
}
