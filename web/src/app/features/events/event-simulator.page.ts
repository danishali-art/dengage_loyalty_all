import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { PageHeader } from '../../shared/ui/page-header';
import { Button } from '../../shared/ui/button';
import { FormErrors } from '../../shared/ui/form-errors';
import { SearchableSelect, SelectOption } from '../../shared/ui/searchable-select';
import { StatusPill } from '../../shared/ui/status-pill';
import { ToastService } from '../../core/ui/toast.service';
import { ApiError } from '../../core/http/api-error';
import { EventsService } from './events.service';
import { EventStatus } from './event.model';

const DEFAULT_DATA_JSON = `{
  "contact_key": "cust_test_1",
  "channel": "web",
  "amount": "1250"
}`;

const STATUS_POLL_INTERVAL_MS = 1500;
const STATUS_POLL_ATTEMPTS = 6;

@Component({
  selector: 'app-event-simulator-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, Button, FormErrors, SearchableSelect, StatusPill, DatePipe],
  template: `
    <app-page-header
      heading="Event Simulator"
      subtitle="Publish a test event straight to RabbitMQ — the same pipeline a real integration uses — to see how it's processed and which rules react."
    />

    <div class="mx-auto max-w-5xl space-y-6 px-8 py-6">
      <app-form-errors [messages]="formErrors()" />

      <section class="card space-y-4">
        <div class="section-header">
          <h2 class="section-heading">Event</h2>
        </div>

        <div>
          <span id="eventType-label" class="field-label">Event type</span>
          <app-searchable-select
            [options]="eventTypeOptions()"
            placeholder="Select an event type…"
            ariaLabelledby="eventType-label"
            [ngModel]="eventType()"
            (ngModelChange)="eventType.set($event)"
          />
          <p class="mt-1.5 text-xs text-gray-500">
            @if (types()) {
              Built-in: {{ types()!.builtIn.join(', ') }}. Generic (deployment-wide, configured in
              <code class="font-mono">RabbitMq:GenericEventTypes</code>): {{ types()!.generic.join(', ') || 'none configured' }}.
            } @else {
              Loading available event types…
            }
          </p>
        </div>

        <div>
          <label for="eventData" class="field-label">Event data (JSON)</label>
          <textarea
            id="eventData"
            rows="8"
            class="field-input resize-y font-mono text-xs"
            [value]="dataJson()"
            (input)="dataJson.set($any($event.target).value)"
            spellcheck="false"
          ></textarea>
          <p class="mt-1.5 text-xs text-gray-500">
            A non-built-in event type requires <code class="font-mono">contact_key</code>,
            <code class="font-mono">channel</code>, and <code class="font-mono">amount</code> (string or number) in the
            data object — the consumer rejects the event otherwise. Built-in types accept whatever fields their own
            schema expects (see the dedicated form for each on the Customers/Programs screens); this simulator sends
            the object as-is.
          </p>
        </div>

        <div class="flex items-center gap-2">
          <app-button [pending]="sending()" [disabled]="!eventType()" (click)="send()">Send event</app-button>
          @if (result()) {
            <app-button variant="ghost" size="sm" (click)="reset()">Send another</app-button>
          }
        </div>
      </section>

      @if (result(); as r) {
        <section class="card space-y-3">
          <div class="section-header">
            <h2 class="section-heading">Result</h2>
          </div>
          <div class="flex items-center justify-between">
            <div>
              <div class="font-mono text-xs text-gray-500">Event id</div>
              <div class="font-mono text-sm text-gray-800">{{ r.eventId }}</div>
            </div>
            @if (status(); as s) {
              <app-status-pill [tone]="statusTone(s.status)" [dot]="true">{{ s.status }}</app-status-pill>
            } @else {
              <span class="inline-flex items-center gap-2 text-xs text-gray-500">
                <span
                  class="h-3.5 w-3.5 animate-spin rounded-full border-2 border-current border-t-transparent"
                  aria-hidden="true"
                ></span>
                Checking…
              </span>
            }
          </div>

          @if (status(); as s) {
            <dl class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
              <dt class="text-gray-500">Event type</dt>
              <dd class="font-mono text-gray-800">{{ s.eventType }}</dd>
              <dt class="text-gray-500">Received</dt>
              <dd class="text-gray-800">{{ s.receivedAt | date: 'medium' }}</dd>
              <dt class="text-gray-500">Processed</dt>
              <dd class="text-gray-800">{{ s.processedAt ? (s.processedAt | date: 'medium') : '—' }}</dd>
              @if (s.error) {
                <dt class="text-gray-500">Error</dt>
                <dd class="text-danger-fg">{{ s.error }}</dd>
              }
            </dl>
            @if (s.status === 'pending') {
              <app-button variant="secondary" size="sm" [pending]="polling()" (click)="pollStatus()">Refresh status</app-button>
            }
          }
        </section>
      }
    </div>
  `,
})
export class EventSimulatorPage implements OnInit {
  private readonly eventsService = inject(EventsService);
  private readonly toast = inject(ToastService);

  protected readonly types = signal<{ builtIn: readonly string[]; generic: readonly string[] } | null>(null);
  protected readonly eventType = signal<string | null>(null);
  protected readonly dataJson = signal(DEFAULT_DATA_JSON);
  protected readonly sending = signal(false);
  protected readonly polling = signal(false);
  protected readonly formErrors = signal<string[]>([]);
  protected readonly result = signal<{ eventId: string } | null>(null);
  protected readonly status = signal<EventStatus | null>(null);

  protected readonly eventTypeOptions = computed<SelectOption<string>[]>(() => {
    const t = this.types();
    if (!t) return [];
    return [...t.builtIn, ...t.generic].map((type) => ({ value: type, label: type }));
  });

  ngOnInit(): void {
    void this.loadTypes();
  }

  private async loadTypes(): Promise<void> {
    try {
      this.types.set(await this.eventsService.getEventTypes());
    } catch {
      this.formErrors.set(['Could not load available event types.']);
    }
  }

  protected async send(): Promise<void> {
    const eventType = this.eventType();
    if (!eventType || this.sending()) return;

    let data: Record<string, unknown>;
    try {
      const parsed: unknown = JSON.parse(this.dataJson());
      if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
        throw new Error('not an object');
      }
      data = parsed as Record<string, unknown>;
    } catch {
      this.formErrors.set(['Event data must be valid JSON, and must be an object (e.g. { "contact_key": "..." }).']);
      return;
    }

    this.sending.set(true);
    this.formErrors.set([]);
    this.result.set(null);
    this.status.set(null);
    try {
      const accepted = await this.eventsService.simulate(eventType, data);
      this.result.set({ eventId: accepted.eventId });
      this.toast.success('Event published', `Accepted as ${accepted.eventId}`);
      void this.pollStatus();
    } catch (err) {
      if (err instanceof ApiError) this.formErrors.set([err.message]);
      else this.formErrors.set(['Something went wrong. Please try again.']);
    } finally {
      this.sending.set(false);
    }
  }

  protected async pollStatus(): Promise<void> {
    const eventId = this.result()?.eventId;
    if (!eventId || this.polling()) return;

    this.polling.set(true);
    try {
      for (let attempt = 0; attempt < STATUS_POLL_ATTEMPTS; attempt++) {
        const current = await this.eventsService.getStatus(eventId);
        this.status.set(current);
        if (current.status !== 'pending') return;
        await new Promise((resolve) => setTimeout(resolve, STATUS_POLL_INTERVAL_MS));
      }
    } catch {
      /* status just stays whatever it last was — a manual "Refresh status" retry is still available */
    } finally {
      this.polling.set(false);
    }
  }

  protected statusTone(status: string): 'success' | 'danger' | 'neutral' {
    if (status === 'processed') return 'success';
    if (status === 'failed') return 'danger';
    return 'neutral';
  }

  protected reset(): void {
    this.result.set(null);
    this.status.set(null);
    this.formErrors.set([]);
  }
}
