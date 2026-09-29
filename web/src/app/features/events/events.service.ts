import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/http/api-client';
import { EventTypesCatalog, EventTypesService } from '../../core/events/event-types.service';
import { EventAccepted, EventStatus } from './event.model';

@Injectable({ providedIn: 'root' })
export class EventsService {
  private readonly api = inject(ApiClient);
  private readonly eventTypes = inject(EventTypesService);

  getEventTypes(): Promise<EventTypesCatalog> {
    return this.eventTypes.get();
  }

  /** Admin-JWT path — same publish pipeline as the API-key `/generic` route, for testing from the portal. */
  simulate(eventType: string, data: Record<string, unknown>): Promise<EventAccepted> {
    return firstValueFrom(
      this.api.tenantScope.post<EventAccepted>('events/simulate', { eventType, data }, { skipErrorToast: true }),
    );
  }

  getStatus(eventId: string): Promise<EventStatus> {
    return firstValueFrom(this.api.tenantScope.get<EventStatus>(`events/${eventId}`));
  }
}
