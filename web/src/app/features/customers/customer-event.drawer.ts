import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { TranslatePipe } from '@ngx-translate/core';
import { ApiError } from '../../core/http/api-error';
import { closeDialogAnimated } from '../../core/ui/dialog.service';
import { SidePanel } from '../../shared/ui/side-panel';
import { StatusPill, StatusTone } from '../../shared/ui/status-pill';
import { CustomersService } from './customers.service';
import { CustomerEventDetail } from './customer.model';
import { isDebit } from './customer-filters';

export interface CustomerEventDrawerData {
  contactKey: string;
  eventId: string;
  /** The Activity row the drawer was opened from — that posting is highlighted. */
  postingId?: string;
}

export function inboxStatusTone(status: string): StatusTone {
  return status === 'processed' ? 'success' : status === 'failed' ? 'danger' : 'warning';
}

/**
 * CR 2026-10-02 (Customer 360, §3.4): everything one event did for this customer — the wallets it
 * hit, the event itself (payload masked by the API), why rules fired, streak/reward/tier effects
 * and the messages it caused (no payload). An id with no inbound event is a scheduled job's.
 */
@Component({
  selector: 'app-customer-event-drawer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SidePanel, StatusPill, DatePipe, TranslatePipe],
  template: `
    <app-side-panel
      [heading]="'customers.drawer.heading' | translate"
      [subheading]="data.eventId"
      [closeLabel]="'customers.drawer.close' | translate"
      [busy]="loading()"
      (closed)="close()"
    >
      @if (loading()) {
        <p class="text-sm text-gray-500">{{ 'common.loading' | translate }}</p>
      } @else if (notFound()) {
        <p class="text-sm text-gray-500">{{ 'customers.drawer.notFound' | translate }}</p>
      } @else if (error()) {
        <p class="text-danger-fg text-sm">{{ error()! | translate }}</p>
      } @else if (detail(); as d) {
        <div class="space-y-6 text-sm">
          <!-- 1. Wallets involved -->
          <section>
            <h3 class="section-label mb-2">{{ 'customers.drawer.wallets' | translate }}</h3>
            @if (d.postings.length === 0 && d.heldPostings.length === 0) {
              <p class="text-gray-500">{{ 'customers.drawer.noPostings' | translate }}</p>
            }
            <ul class="space-y-2">
              @for (p of d.postings; track p.id) {
                <li
                  class="rounded-lg border px-3 py-2"
                  [class]="
                    p.id === data.postingId ? 'border-brand bg-brand-light' : 'border-gray-200'
                  "
                >
                  <div class="flex items-center justify-between gap-2">
                    <span class="font-medium text-gray-800">
                      {{ p.accountTypeName }}
                      <span class="text-xs text-gray-400"
                        >· {{ p.accountTypeType }} · {{ p.programName }}</span
                      >
                    </span>
                    <span
                      class="font-medium"
                      [class]="debit(p.delta) ? 'text-danger-fg' : 'text-success-fg'"
                    >
                      {{ debit(p.delta) ? '' : '+' }}{{ p.delta }}
                    </span>
                  </div>
                  <div class="mt-1 text-xs text-gray-500">
                    {{ 'customers.reason.' + p.reason | translate }}
                    @if (p.ruleName) {
                      ·
                      {{
                        'customers.source.rule'
                          | translate: { name: p.ruleName, version: p.ruleVersion ?? '—' }
                      }}
                    }
                    @if (p.campaignName) {
                      · {{ 'customers.source.streak' | translate: { name: p.campaignName } }}
                    }
                    @if (p.contactKey !== data.contactKey) {
                      ·
                      {{
                        'customers.drawer.counterparty' | translate: { contactKey: p.contactKey }
                      }}
                    }
                  </div>
                </li>
              }
              @for (h of d.heldPostings; track h.id) {
                <li class="rounded-lg border border-dashed border-gray-300 px-3 py-2">
                  <div class="flex items-center justify-between gap-2">
                    <span class="font-medium text-gray-800">{{ h.accountTypeName }}</span>
                    <span class="text-gray-600">{{ h.delta }}</span>
                  </div>
                  <div class="mt-1 text-xs text-gray-500">
                    @if (h.cancelledAt) {
                      {{
                        'customers.drawer.heldCancelled'
                          | translate: { date: (h.cancelledAt | date: 'medium') }
                      }}
                    } @else if (h.postedAt) {
                      {{
                        'customers.drawer.heldPosted'
                          | translate: { date: (h.postedAt | date: 'medium') }
                      }}
                    } @else {
                      {{
                        'customers.drawer.heldUntil'
                          | translate: { date: (h.holdUntil | date: 'medium') }
                      }}
                    }
                    @if (h.refundedDelta && !h.cancelledAt) {
                      ·
                      {{ 'customers.drawer.heldRefunded' | translate: { amount: h.refundedDelta } }}
                    }
                    @if (h.ruleName) {
                      · {{ h.ruleName }}
                    }
                  </div>
                </li>
              }
            </ul>
          </section>

          <!-- 2. Event -->
          <section>
            <h3 class="section-label mb-2">{{ 'customers.drawer.event' | translate }}</h3>
            @if (d.event; as e) {
              <dl class="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 text-xs">
                <dt class="text-gray-500">{{ 'customers.col.eventType' | translate }}</dt>
                <dd class="font-mono text-gray-800">{{ e.eventType }}</dd>
                <dt class="text-gray-500">{{ 'customers.col.status' | translate }}</dt>
                <dd>
                  <app-status-pill [tone]="statusTone(e.status)" [dot]="true">{{
                    'customers.status.' + e.status | translate
                  }}</app-status-pill>
                </dd>
                <dt class="text-gray-500">{{ 'customers.col.occurred' | translate }}</dt>
                <dd>{{ e.occurredAt ? (e.occurredAt | date: 'medium') : '—' }}</dd>
                <dt class="text-gray-500">{{ 'customers.col.received' | translate }}</dt>
                <dd>{{ e.receivedAt | date: 'medium' }}</dd>
                <dt class="text-gray-500">{{ 'customers.col.processed' | translate }}</dt>
                <dd>{{ e.processedAt ? (e.processedAt | date: 'medium') : '—' }}</dd>
                @if (e.error) {
                  <dt class="text-gray-500">{{ 'customers.col.error' | translate }}</dt>
                  <dd class="text-danger-fg font-mono">{{ e.error }}</dd>
                }
              </dl>
              <div class="mt-3">
                <div class="mb-1 flex items-center justify-between">
                  <span class="text-xs text-gray-500">{{
                    'customers.drawer.payload' | translate
                  }}</span>
                  <button
                    type="button"
                    class="cursor-pointer rounded px-2 py-0.5 text-xs text-gray-500 hover:bg-gray-100"
                    (click)="copyPayload()"
                  >
                    {{
                      (copied() ? 'customers.drawer.copied' : 'customers.drawer.copy') | translate
                    }}
                  </button>
                </div>
                <pre
                  class="max-h-72 overflow-auto rounded-lg bg-gray-50 p-3 font-mono text-xs text-gray-800"
                  >{{ payloadText() }}</pre>
                <p class="mt-1 text-xs text-gray-400">
                  {{ 'customers.drawer.masked' | translate }}
                </p>
              </div>
            } @else {
              <p class="text-gray-500">{{ 'customers.drawer.noInbound' | translate }}</p>
            }
          </section>

          <!-- 3. Why -->
          <section>
            <h3 class="section-label mb-2">{{ 'customers.drawer.why' | translate }}</h3>
            @if (d.ruleFires.length === 0) {
              <p class="text-gray-500">{{ 'customers.drawer.noRuleFires' | translate }}</p>
            }
            @for (s of d.onceOnlySkips ?? []; track s.ruleId) {
              <p
                class="mb-2 rounded-lg border border-dashed border-gray-300 px-3 py-2 text-gray-600"
              >
                {{
                  'customers.drawer.onceOnlySkip'
                    | translate
                      : {
                          name: s.ruleName ?? s.ruleId,
                          date: (s.earlierAt | date: 'medium'),
                          eventId: s.earlierEventId,
                        }
                }}
              </p>
            }
            @for (f of d.ruleFires; track f.id) {
              <details class="mb-2 rounded-lg border border-gray-200 px-3 py-2">
                <summary class="cursor-pointer">
                  {{
                    'customers.source.rule'
                      | translate: { name: f.ruleName ?? f.ruleId, version: f.ruleVersion }
                  }}
                  · {{ f.resultingDelta }}
                </summary>
                @for (snap of snapshots(f); track snap.label) {
                  <p class="mt-2 text-xs text-gray-500">{{ snap.label | translate }}</p>
                  <pre class="overflow-auto rounded bg-gray-50 p-2 font-mono text-xs">{{
                    snap.json
                  }}</pre>
                }
              </details>
            }
          </section>

          <!-- 4. Everything else this event did -->
          <section>
            <h3 class="section-label mb-2">{{ 'customers.drawer.effects' | translate }}</h3>
            @if (!hasEffects()) {
              <p class="text-gray-500">{{ 'customers.drawer.noEffects' | translate }}</p>
            }
            <ul class="space-y-1 text-xs text-gray-700">
              @for (s of d.streaksApplied; track s.campaignId) {
                <li>
                  {{
                    'customers.drawer.streakApplied'
                      | translate: { name: s.campaignName ?? s.campaignId }
                  }}
                </li>
              }
              @for (c of d.streakCompletions; track c.campaignId + c.completionNo) {
                <li>
                  {{
                    'customers.drawer.streakCompleted'
                      | translate
                        : {
                            name: c.campaignName ?? c.campaignId,
                            n: c.completionNo,
                            reward: c.rewardKind,
                          }
                  }}
                </li>
              }
              @for (r of d.rewards; track r.id) {
                <li>
                  {{
                    'customers.drawer.reward' | translate: { name: r.rewardName, status: r.status }
                  }}
                </li>
              }
              @for (t of d.tierChanges; track t.id) {
                <li>
                  {{
                    'customers.drawer.tierChange'
                      | translate: { from: t.fromTierName ?? '—', to: t.toTierName }
                  }}
                </li>
              }
            </ul>
          </section>

          <!-- 5. Messages sent (D5: no payload) -->
          <section>
            <h3 class="section-label mb-2">{{ 'customers.drawer.messages' | translate }}</h3>
            @if (d.messages.length === 0) {
              <p class="text-gray-500">{{ 'customers.drawer.noMessages' | translate }}</p>
            }
            <ul class="space-y-1 text-xs">
              @for (m of d.messages; track m.eventId) {
                <li class="flex items-center justify-between gap-2">
                  <span class="font-mono text-gray-700">{{ m.eventType }}</span>
                  <span class="text-gray-500"
                    >{{ m.status }} · {{ m.createdAt | date: 'medium' }}</span
                  >
                </li>
              }
            </ul>
          </section>
        </div>
      }
    </app-side-panel>
  `,
})
export class CustomerEventDrawer implements OnInit {
  protected readonly data = inject<CustomerEventDrawerData>(DIALOG_DATA);
  private readonly ref = inject(DialogRef);
  private readonly customers = inject(CustomersService);

  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly detail = signal<CustomerEventDetail | null>(null);
  protected readonly copied = signal(false);
  protected readonly debit = isDebit;
  protected readonly statusTone = inboxStatusTone;

  protected readonly payloadText = computed(() =>
    JSON.stringify(this.detail()?.event?.data ?? null, null, 2),
  );
  protected readonly hasEffects = computed(() => {
    const d = this.detail();
    return (
      !!d &&
      d.streaksApplied.length +
        d.streakCompletions.length +
        d.rewards.length +
        d.tierChanges.length >
        0
    );
  });

  ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.detail.set(await this.customers.getEvent(this.data.contactKey, this.data.eventId));
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) this.notFound.set(true);
      else this.error.set('customers.loadError');
    } finally {
      this.loading.set(false);
    }
  }

  protected snapshots(f: {
    conditionsSnapshot: string | null;
    calculationSnapshot: string;
    resolutionSnapshot: string | null;
  }): { label: string; json: string }[] {
    return [
      { label: 'customers.drawer.conditions', raw: f.conditionsSnapshot },
      { label: 'customers.drawer.calculation', raw: f.calculationSnapshot },
      { label: 'customers.drawer.resolution', raw: f.resolutionSnapshot },
    ]
      .filter((s): s is { label: string; raw: string } => !!s.raw)
      .map((s) => ({ label: s.label, json: prettyJson(s.raw) }));
  }

  protected async copyPayload(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.payloadText());
      this.copied.set(true);
    } catch {
      // Clipboard can be unavailable (insecure context); the payload is still selectable.
    }
  }

  protected close(): void {
    closeDialogAnimated(this.ref);
  }
}

function prettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}
