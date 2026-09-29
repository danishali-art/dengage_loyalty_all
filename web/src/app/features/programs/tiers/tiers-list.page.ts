import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { PageHeader } from '../../../shared/ui/page-header';
import { Button } from '../../../shared/ui/button';
import { IconButton } from '../../../shared/ui/icon-button';
import { EmptyState } from '../../../shared/ui/empty-state';
import { DialogService } from '../../../core/ui/dialog.service';
import { ToastService } from '../../../core/ui/toast.service';
import { ConfirmService } from '../../../core/ui/confirm.service';
import { ApiError } from '../../../core/http/api-error';
import { ProgramContextStore } from '../../../core/program/program-context.store';
import { ProgramsService } from '../programs.service';
import { TiersService } from './tiers.service';
import { Tier } from './tier.model';
import { TierFormDialog, TierFormData } from './tier-form.dialog';

@Component({
  selector: 'app-tiers-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, Button, IconButton, EmptyState],
  template: `
    <app-page-header
      heading="Tier Levels"
      subtitle="Assigned automatically based on the qualifying points threshold."
      [crumbs]="[
        { label: 'Programs', link: ['/programs'] },
        { label: programName(), link: ['/programs', programId()] },
        { label: 'Tiers' },
      ]"
    >
      <div actions>
        <app-button (click)="create()">
          <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true"><path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" /></svg>
          Add tier
        </app-button>
      </div>
    </app-page-header>

    <div class="mx-auto max-w-5xl px-8 py-6">
      <div class="card !p-0">
        @if (loading() && tiers().length === 0) {
          <div class="overflow-x-auto" aria-busy="true">
            <table class="w-full text-sm">
              <thead>
                <tr>
                  <th class="section-label px-4 py-3 text-left">Order</th>
                  <th class="section-label px-4 py-3 text-left">Name</th>
                  <th class="section-label px-4 py-3 text-right">Min points</th>
                  <th class="section-label px-4 py-3 text-left">Qualifying</th>
                  <th class="section-label px-4 py-3 text-left">Grace</th>
                  <th class="px-4 py-3"></th>
                </tr>
              </thead>
              <tbody>
                @for (i of [0, 1, 2, 3]; track i) {
                  <tr class="border-t border-gray-100" aria-hidden="true">
                    <td class="px-4 py-3.5"><div class="h-6 w-6 animate-pulse rounded-full bg-gray-200"></div></td>
                    <td class="px-4 py-3.5"><div class="h-4 w-32 animate-pulse rounded bg-gray-200"></div></td>
                    <td class="px-4 py-3.5"><div class="ml-auto h-4 w-14 animate-pulse rounded bg-gray-200"></div></td>
                    <td class="px-4 py-3.5"><div class="h-4 w-24 animate-pulse rounded bg-gray-200"></div></td>
                    <td class="px-4 py-3.5"><div class="h-4 w-16 animate-pulse rounded bg-gray-200"></div></td>
                    <td class="px-4 py-3.5"></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else if (!loading() && tiers().length === 0) {
          <div class="p-5">
            <app-empty-state heading="No tiers yet" description="Create at least two to give customers something to work toward." icon="🏅" />
          </div>
        } @else {
          <div class="overflow-x-auto">
            <table class="w-full text-sm" [attr.aria-busy]="loading() ? 'true' : null">
              <thead>
                <tr>
                  <th class="section-label px-4 py-3 text-left">Order</th>
                  <th class="section-label px-4 py-3 text-left">Name</th>
                  <th class="section-label px-4 py-3 text-right">Min points</th>
                  <th class="section-label px-4 py-3 text-left">Qualifying</th>
                  <th class="section-label px-4 py-3 text-left">Grace</th>
                  <th class="px-4 py-3"></th>
                </tr>
              </thead>
              <tbody>
                @for (tier of tiers(); track tier.id; let i = $index) {
                  <tr class="border-t border-gray-100 hover:bg-gray-50">
                    <td class="px-4 py-3.5">
                      <div class="flex items-center gap-1">
                        <span class="flex h-6 w-6 items-center justify-center rounded-full bg-gray-100 text-xs font-medium text-gray-500">{{ i + 1 }}</span>
                        <app-icon-button ariaLabel="Move up" [disabled]="i === 0" (click)="moveTier(i, -1)">↑</app-icon-button>
                        <app-icon-button ariaLabel="Move down" [disabled]="i === tiers().length - 1" (click)="moveTier(i, 1)">↓</app-icon-button>
                      </div>
                    </td>
                    <td class="px-4 py-3.5">{{ tier.displayName }} <span class="font-mono text-xs text-gray-400">({{ tier.name }})</span></td>
                    <td class="px-4 py-3.5 text-right font-medium">{{ tier.minPoints }}</td>
                    <td class="px-4 py-3.5 text-xs text-gray-500">
                      {{ tier.qualifyingModel === 'periodic' ? 'Last ' + tier.qualifyingPeriodDays + ' days' : 'Lifetime' }}
                    </td>
                    <td class="px-4 py-3.5 text-xs text-gray-500">{{ tier.graceDays }} days</td>
                    <td class="px-4 py-3.5 text-right whitespace-nowrap">
                      <button type="button" class="cursor-pointer rounded px-2 py-1 text-xs text-gray-400 transition-colors hover:bg-gray-100 hover:text-brand" (click)="edit(tier)">Edit</button>
                      <button
                        type="button"
                        class="cursor-pointer rounded px-2 py-1 text-xs text-gray-400 transition-colors hover:bg-danger-bg hover:text-danger-fg disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent"
                        [disabled]="!canDelete(tier)"
                        [attr.title]="deleteBlockedReason(tier)"
                        (click)="remove(tier)"
                      >
                        Delete
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    </div>
  `,
})
export class TiersListPage implements OnInit {
  readonly programId = input.required<string>();

  private readonly tiersService = inject(TiersService);
  private readonly programsService = inject(ProgramsService);
  private readonly dialog = inject(DialogService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly programContext = inject(ProgramContextStore);

  protected readonly tiers = signal<readonly Tier[]>([]);
  protected readonly loading = signal(true);
  protected readonly programIsActive = signal(false);
  protected readonly programName = (): string => this.programContext.program()?.name ?? 'Program';

  ngOnInit(): void {
    void this.reload();
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    try {
      const [result, program] = await Promise.all([
        this.tiersService.list(this.programId()),
        this.programsService.get(this.programId()),
      ]);
      this.tiers.set([...result.data].sort((a, b) => a.sortOrder - b.sortOrder));
      this.programIsActive.set(program.status === 'active');
    } finally {
      this.loading.set(false);
    }
  }

  // Client-side pre-emption of the same guards TiersAppService.DeleteAsync enforces —
  // the server remains authoritative, this only avoids a round-trip to learn "no".
  protected canDelete(tier: Tier): boolean {
    return !tier.hasAssignedAccounts && !this.programIsActive();
  }

  protected deleteBlockedReason(tier: Tier): string | null {
    if (tier.hasAssignedAccounts) return 'This tier has customer accounts assigned to it.';
    if (this.programIsActive()) return 'The program must be inactive before its tiers can be deleted.';
    return null;
  }

  protected async remove(tier: Tier): Promise<void> {
    if (!this.canDelete(tier)) return;
    const confirmed = await this.confirm.ask({
      title: 'Delete tier?',
      message: `"${tier.displayName}" will be removed. This cannot be undone.`,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!confirmed) return;

    try {
      await this.tiersService.delete(this.programId(), tier.id);
      this.toast.success(`Tier "${tier.displayName}" deleted`);
      await this.reload();
    } catch (err) {
      if (err instanceof ApiError) this.toast.error(err.message);
      else this.toast.error('Something went wrong. Please try again.');
    }
  }

  protected create(): void {
    const nextSortOrder = this.tiers().length;
    const data: TierFormData = { programId: this.programId(), nextSortOrder };
    const ref = this.dialog.open<Tier, TierFormData, TierFormDialog>(TierFormDialog, { data });
    ref.closed.subscribe((result) => {
      if (!result) return;
      this.toast.success(`Tier "${result.displayName}" created`);
      void this.reload();
    });
  }

  protected edit(existing: Tier): void {
    const data: TierFormData = { programId: this.programId(), nextSortOrder: this.tiers().length, existing };
    const ref = this.dialog.open<Tier, TierFormData, TierFormDialog>(TierFormDialog, { data });
    ref.closed.subscribe((result) => {
      if (!result) return;
      this.toast.success('Tier saved');
      void this.reload();
    });
  }

  protected async moveTier(index: number, direction: -1 | 1): Promise<void> {
    const ordered = [...this.tiers()];
    const target = index + direction;
    if (target < 0 || target >= ordered.length) return;
    const a = ordered[index];
    const b = ordered[target];
    if (!a || !b) return;
    ordered[index] = b;
    ordered[target] = a;
    this.tiers.set(ordered);
    await this.tiersService.reorder(this.programId(), ordered.map((t) => t.id));
    await this.reload();
  }
}
