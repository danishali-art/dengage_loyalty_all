import { ChangeDetectionStrategy, Component, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { IconButton } from '../../shared/ui/icon-button';

/** The eye icon that opens the event drawer (CR 2026-10-02, §3.4). */
@Component({
  selector: 'app-customer-event-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [IconButton, TranslatePipe],
  template: `
    <app-icon-button
      [ariaLabel]="'customers.activity.openEvent' | translate"
      (click)="opened.emit()"
    >
      <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true">
        <path d="M10 12.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z" />
        <path
          fill-rule="evenodd"
          d="M.66 10.59a1.65 1.65 0 0 1 0-1.18A10 10 0 0 1 10 3c4.26 0 7.9 2.66 9.34 6.41.15.38.15.8 0 1.18A10 10 0 0 1 10 17c-4.26 0-7.9-2.66-9.34-6.41ZM14 10a4 4 0 1 1-8 0 4 4 0 0 1 8 0Z"
          clip-rule="evenodd"
        />
      </svg>
    </app-icon-button>
  `,
})
export class CustomerEventButton {
  readonly opened = output<void>();
}

/** What a tab asks the page to open in the drawer. */
export interface OpenEventRequest {
  eventId: string;
  postingId?: string;
}
