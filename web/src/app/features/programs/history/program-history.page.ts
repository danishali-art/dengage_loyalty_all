import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { PageHeader } from '../../../shared/ui/page-header';
import { StatusPill, StatusTone } from '../../../shared/ui/status-pill';
import { EmptyState } from '../../../shared/ui/empty-state';
import { DialogService } from '../../../core/ui/dialog.service';
import { ProgramContextStore } from '../../../core/program/program-context.store';
import { ProgramHistoryService } from './program-history.service';
import { ConfigVersionSummary } from './program-history.model';
import { ViewVersionDialog } from './view-version.dialog';

/** Mirrors ProgramsAppService.PublicationEntityType (1.3.CL item 9). */
const PUBLICATION_ENTITY_TYPE = 'ProgramPublication';

const CHANGE_TONE: Record<ConfigVersionSummary['changeType'], StatusTone> = {
  created: 'success',
  updated: 'info',
  deleted: 'danger',
  published: 'success',
};

@Component({
  selector: 'app-program-history-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, TranslatePipe, PageHeader, StatusPill, EmptyState],
  template: `
    <app-page-header
      heading="History"
      subtitle="Every saved change to this program's configuration."
      [crumbs]="[
        { label: 'Programs', link: ['/programs'] },
        { label: programName(), link: ['/programs', programId()] },
        { label: 'History' },
      ]"
    />

    <div class="mx-auto max-w-3xl space-y-6 px-8 py-6">
      <!-- 1.3.CL item 9: one entry per Publish, each a snapshot of the whole program. -->
      <section class="card !p-0" aria-labelledby="published-heading">
        <h2 id="published-heading" class="section-heading px-4 pt-4">
          {{ 'history.published.heading' | translate }}
        </h2>
        @if (!loading() && publications().length === 0) {
          <p class="px-4 py-3.5 text-sm text-gray-500">
            {{ 'history.published.empty' | translate }}
          </p>
        } @else {
          <ul class="divide-y divide-gray-100" [attr.aria-busy]="loading() ? 'true' : null">
            @for (v of publications(); track v.id) {
              <li class="flex items-center justify-between gap-3 px-4 py-3.5">
                <div class="flex items-center gap-3">
                  <app-status-pill tone="success" [dot]="true"
                    >v{{ v.versionNumber }}</app-status-pill
                  >
                  <div>
                    <div class="text-sm text-gray-800">
                      {{ 'history.published.by' | translate: { by: v.changedBy } }}
                    </div>
                    <div class="text-xs text-gray-400">
                      {{ v.changedAt | date: 'medium' }}
                      @if (v.changeSummary) {
                        · {{ v.changeSummary }}
                      }
                    </div>
                  </div>
                </div>
                <button
                  type="button"
                  class="text-brand cursor-pointer text-xs font-medium hover:underline"
                  (click)="view(v)"
                >
                  View
                </button>
              </li>
            }
          </ul>
        }
      </section>

      <h2 class="section-heading">{{ 'history.changes.heading' | translate }}</h2>
      <div class="card !p-0">
        @if (loading()) {
          <ul class="divide-y divide-gray-100" aria-busy="true">
            @for (i of [0, 1, 2, 3, 4]; track i) {
              <li class="flex items-center justify-between gap-3 px-4 py-3.5" aria-hidden="true">
                <div class="flex items-center gap-3">
                  <div class="h-5 w-10 animate-pulse rounded-full bg-gray-200"></div>
                  <div>
                    <div class="h-4 w-48 animate-pulse rounded bg-gray-200"></div>
                    <div class="mt-1.5 h-3 w-28 animate-pulse rounded bg-gray-200"></div>
                  </div>
                </div>
                <div class="h-3 w-8 animate-pulse rounded bg-gray-200"></div>
              </li>
            }
          </ul>
        } @else if (versions().length === 0) {
          <div class="p-5">
            <app-empty-state
              heading="No history yet"
              description="Every save from now on will appear here."
              icon="🕓"
            />
          </div>
        } @else {
          <ul class="divide-y divide-gray-100">
            @for (v of versions(); track v.id) {
              <li class="flex items-center justify-between gap-3 px-4 py-3.5">
                <div class="flex items-center gap-3">
                  <app-status-pill [tone]="tone(v.changeType)" [dot]="true"
                    >v{{ v.versionNumber }}</app-status-pill
                  >
                  <div>
                    <div class="text-sm text-gray-800">{{ v.changeType }} by {{ v.changedBy }}</div>
                    <div class="text-xs text-gray-400">{{ v.changedAt | date: 'medium' }}</div>
                  </div>
                </div>
                <button
                  type="button"
                  class="text-brand cursor-pointer text-xs font-medium hover:underline"
                  (click)="view(v)"
                >
                  View
                </button>
              </li>
            }
          </ul>
        }
      </div>
    </div>
  `,
})
export class ProgramHistoryPage implements OnInit {
  readonly programId = input.required<string>();

  private readonly historyService = inject(ProgramHistoryService);
  private readonly dialog = inject(DialogService);
  private readonly programContext = inject(ProgramContextStore);

  protected readonly loading = signal(true);
  protected readonly versions = signal<readonly ConfigVersionSummary[]>([]);
  protected readonly publications = signal<readonly ConfigVersionSummary[]>([]);
  protected readonly programName = (): string => this.programContext.program()?.name ?? 'Program';

  ngOnInit(): void {
    void this.reload();
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    try {
      // Per-edit rows ('Program') are kept alongside the publish snapshots (§5 f).
      const [versions, publications] = await Promise.all([
        this.historyService.list('Program', this.programId()),
        this.historyService.list(PUBLICATION_ENTITY_TYPE, this.programId()),
      ]);
      this.versions.set(versions);
      this.publications.set(publications);
    } finally {
      this.loading.set(false);
    }
  }

  protected tone(changeType: ConfigVersionSummary['changeType']): StatusTone {
    return CHANGE_TONE[changeType];
  }

  protected async view(summary: ConfigVersionSummary): Promise<void> {
    const detail = await this.historyService.get(summary.id);
    this.dialog.open(ViewVersionDialog, { data: detail });
  }
}
