import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { DialogRef, DIALOG_DATA } from '@angular/cdk/dialog';
import { TranslatePipe } from '@ngx-translate/core';
import { closeDialogAnimated } from '../../../core/ui/dialog.service';
import { DialogShell } from '../../../shared/ui/dialog-shell';
import { Button } from '../../../shared/ui/button';
import { ConfigVersionDetail } from './program-history.model';

/** Top-level keys of the ProgramPublicationSnapshot (ProgramsDtos.cs), in display order. */
const PUBLICATION_SECTIONS = [
  'Program',
  'AccountTypes',
  'Tiers',
  'Rewards',
  'Rules',
  'StreakCampaigns',
] as const;

interface SnapshotSection {
  key: string;
  labelKey: string;
  count: number | null;
  json: string;
}

@Component({
  selector: 'app-view-version-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, TranslatePipe, DialogShell, Button],
  template: `
    <app-dialog-shell [heading]="'Version ' + data.versionNumber" (closed)="closeAnimated()">
      <p class="mb-3 text-xs text-gray-500">
        {{ data.changeType }} by {{ data.changedBy }} — {{ data.changedAt | date: 'medium' }}
      </p>
      @if (sections(); as list) {
        <!-- 1.3.CL item 9: a publish snapshot covers the whole program — one collapsible section per part. -->
        @if (data.changeSummary) {
          <p class="mb-3 text-sm text-gray-700">{{ data.changeSummary }}</p>
        }
        <div class="max-h-96 space-y-2 overflow-auto">
          @for (s of list; track s.key) {
            <details class="rounded-lg border border-gray-200">
              <summary class="cursor-pointer px-3 py-2 text-sm font-medium text-gray-800">
                {{ s.labelKey | translate }}
                @if (s.count !== null) {
                  <span class="text-gray-400">({{ s.count }})</span>
                }
              </summary>
              <pre class="overflow-auto bg-gray-50 p-3 text-xs">{{ s.json }}</pre>
            </details>
          }
        </div>
      } @else {
        <pre class="max-h-96 overflow-auto rounded-lg bg-gray-50 p-3 text-xs">{{ pretty() }}</pre>
      }
      <app-button footer variant="secondary" (click)="closeAnimated()">Close</app-button>
    </app-dialog-shell>
  `,
})
export class ViewVersionDialog {
  readonly ref = inject<DialogRef<void, ViewVersionDialog>>(DialogRef);
  protected readonly data = inject<ConfigVersionDetail>(DIALOG_DATA);

  protected readonly pretty = computed(() => {
    try {
      return JSON.stringify(JSON.parse(this.data.snapshot), null, 2);
    } catch {
      return this.data.snapshot;
    }
  });

  /** Non-null only for a publish snapshot; per-edit rows keep the raw JSON view. */
  protected readonly sections = computed((): SnapshotSection[] | null => {
    if (this.data.changeType !== 'published') return null;
    try {
      const snapshot = JSON.parse(this.data.snapshot) as Record<string, unknown>;
      return PUBLICATION_SECTIONS.filter((key) => key in snapshot).map((key) => {
        const value = snapshot[key];
        return {
          key,
          labelKey: `history.snapshot.${key}`,
          count: Array.isArray(value) ? value.length : null,
          json: JSON.stringify(value, null, 2),
        };
      });
    } catch {
      return null;
    }
  });

  protected closeAnimated(): void {
    closeDialogAnimated(this.ref);
  }
}
