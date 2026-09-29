import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { PageHeader } from '../../../shared/ui/page-header';
import { Button } from '../../../shared/ui/button';
import { StatusPill } from '../../../shared/ui/status-pill';
import { EmptyState } from '../../../shared/ui/empty-state';
import { Paginator } from '../../../shared/ui/paginator';
import { DialogService } from '../../../core/ui/dialog.service';
import { ToastService } from '../../../core/ui/toast.service';
import { ProgramContextStore } from '../../../core/program/program-context.store';
import { RewardsService } from './rewards.service';
import { Reward } from './reward.model';
import { RewardFormDialog, RewardFormData } from './reward-form.dialog';
import { AccountTypesService } from '../account-types/account-types.service';
import { AccountType } from '../account-types/account-type.model';
import { TiersService } from '../tiers/tiers.service';
import { Tier } from '../tiers/tier.model';

const PAGE_SIZE = 12;

@Component({
  selector: 'app-rewards-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, Button, StatusPill, EmptyState, Paginator],
  template: `
    <app-page-header
      heading="Rewards"
      [crumbs]="[
        { label: 'Programs', link: ['/programs'] },
        { label: programName(), link: ['/programs', programId()] },
        { label: 'Rewards' },
      ]"
    >
      <div actions>
        <app-button (click)="create()">
          <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true"><path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" /></svg>
          Add reward
        </app-button>
      </div>
    </app-page-header>

    <div class="mx-auto max-w-7xl px-8 py-6">
      <div class="card !p-0">
        @if (loading() && rewards().length === 0) {
          <div class="overflow-x-auto" aria-busy="true">
            <table class="w-full text-sm">
              <thead>
                <tr>
                  <th class="section-label px-4 py-3 text-left">Name</th>
                  <th class="section-label px-4 py-3 text-left">Type</th>
                  <th class="section-label px-4 py-3 text-left">Acquisition</th>
                  <th class="section-label px-4 py-3 text-left">Status</th>
                  <th class="px-4 py-3"></th>
                </tr>
              </thead>
              <tbody>
                @for (i of [0, 1, 2, 3, 4]; track i) {
                  <tr class="border-t border-gray-100" aria-hidden="true">
                    <td class="px-4 py-3.5"><div class="h-4 w-36 animate-pulse rounded bg-gray-200"></div></td>
                    <td class="px-4 py-3.5"><div class="h-4 w-20 animate-pulse rounded bg-gray-200"></div></td>
                    <td class="px-4 py-3.5"><div class="h-4 w-24 animate-pulse rounded bg-gray-200"></div></td>
                    <td class="px-4 py-3.5"><div class="h-5 w-16 animate-pulse rounded-full bg-gray-200"></div></td>
                    <td class="px-4 py-3.5"></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else if (!loading() && rewards().length === 0) {
          <div class="p-5">
            <app-empty-state heading="No rewards yet" description="Create one so customers have something to redeem." icon="🎁" />
          </div>
        } @else {
          <div class="overflow-x-auto">
            <table class="w-full text-sm" [attr.aria-busy]="loading() ? 'true' : null">
              <thead>
                <tr>
                  <th class="section-label px-4 py-3 text-left">Name</th>
                  <th class="section-label px-4 py-3 text-left">Type</th>
                  <th class="section-label px-4 py-3 text-left">Acquisition</th>
                  <th class="section-label px-4 py-3 text-left">Status</th>
                  <th class="px-4 py-3"></th>
                </tr>
              </thead>
              <tbody>
                @for (reward of rewards(); track reward.id) {
                  <tr class="border-t border-gray-100 hover:bg-gray-50">
                    <td class="px-4 py-3.5">{{ reward.displayName }} <span class="font-mono text-xs text-gray-400">({{ reward.name }})</span></td>
                    <td class="px-4 py-3.5 font-mono text-xs text-gray-600">{{ reward.rewardType }}</td>
                    <td class="px-4 py-3.5 text-xs text-gray-500">{{ reward.acquisition }}</td>
                    <td class="px-4 py-3.5">
                      <app-status-pill [tone]="reward.isActive ? 'success' : 'neutral'" [dot]="true">
                        {{ reward.isActive ? 'Active' : 'Inactive' }}
                      </app-status-pill>
                    </td>
                    <td class="px-4 py-3.5 text-right">
                      <button type="button" class="cursor-pointer rounded px-2 py-1 text-xs text-gray-400 transition-colors hover:bg-gray-100 hover:text-brand" (click)="edit(reward)">Edit</button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="px-4">
            <app-paginator [page]="page()" [pageSize]="pageSize" [total]="total()" (pageChange)="loadPage($event)" />
          </div>
        }
      </div>
    </div>
  `,
})
export class RewardsListPage implements OnInit {
  readonly programId = input.required<string>();

  private readonly rewardsService = inject(RewardsService);
  private readonly accountTypesService = inject(AccountTypesService);
  private readonly tiersService = inject(TiersService);
  private readonly dialog = inject(DialogService);
  private readonly toast = inject(ToastService);
  private readonly programContext = inject(ProgramContextStore);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly rewards = signal<readonly Reward[]>([]);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly programName = (): string => this.programContext.program()?.name ?? 'Program';

  private accountTypes: readonly AccountType[] = [];
  private tiers: readonly Tier[] = [];

  ngOnInit(): void {
    void this.loadPage(1);
    void this.accountTypesService.listAll(this.programId()).then((list) => (this.accountTypes = list));
    void this.tiersService.list(this.programId()).then((result) => (this.tiers = result.data));
  }

  protected async loadPage(page: number): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.rewardsService.list(this.programId(), page, this.pageSize);
      this.rewards.set(result.data);
      this.page.set(result.page);
      this.total.set(result.total);
    } finally {
      this.loading.set(false);
    }
  }

  protected create(): void {
    const data: RewardFormData = { programId: this.programId(), accountTypes: this.accountTypes, tiers: this.tiers };
    const ref = this.dialog.open<Reward, RewardFormData, RewardFormDialog>(RewardFormDialog, { data });
    ref.closed.subscribe((result) => {
      if (!result) return;
      this.toast.success(`Reward "${result.displayName}" created`);
      void this.loadPage(1);
    });
  }

  protected edit(existing: Reward): void {
    const data: RewardFormData = { programId: this.programId(), accountTypes: this.accountTypes, tiers: this.tiers, existing };
    const ref = this.dialog.open<Reward, RewardFormData, RewardFormDialog>(RewardFormDialog, { data });
    ref.closed.subscribe((result) => {
      if (!result) return;
      this.toast.success('Reward saved');
      void this.loadPage(this.page());
    });
  }
}
