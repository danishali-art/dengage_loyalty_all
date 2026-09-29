import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { PageHeader } from '../../../shared/ui/page-header';
import { DataTable, Column } from '../../../shared/ui/data-table';
import { StatusPill } from '../../../shared/ui/status-pill';
import { Button } from '../../../shared/ui/button';
import { IconButton } from '../../../shared/ui/icon-button';
import { Paginator } from '../../../shared/ui/paginator';
import { ConfirmService } from '../../../core/ui/confirm.service';
import { ToastService } from '../../../core/ui/toast.service';
import { ProgramContextStore } from '../../../core/program/program-context.store';
import { StreakCampaignsService } from './streak-campaigns.service';
import { StreakCampaign } from './streak-campaign.model';

const PAGE_SIZE = 20;

@Component({
  selector: 'app-streak-campaigns-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, DataTable, StatusPill, Button, IconButton, Paginator],
  template: `
    <app-page-header
      heading="Streak Campaigns"
      [crumbs]="[
        { label: 'Programs', link: ['/programs'] },
        { label: programName(), link: ['/programs', programId()] },
        { label: 'Streak Campaigns' },
      ]"
    >
      <div actions>
        <app-button (click)="create()">
          <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true"><path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" /></svg>
          New campaign
        </app-button>
      </div>
    </app-page-header>

    <div class="mx-auto max-w-5xl px-8 py-6">
      <div class="card !p-0">
        <app-data-table
          [columns]="columns"
          [rows]="campaigns()"
          caption="Streak Campaigns"
          [busy]="loading()"
          emptyText="No streak campaigns yet — create one to start rewarding consecutive activity."
          [trackKey]="trackById"
        >
          <ng-template #row let-campaign>
            <td class="cursor-pointer px-4 py-3.5 font-medium text-gray-800" [routerLink]="['/programs', programId(), 'streak-campaigns', campaign.id]">
              {{ campaign.name }}
            </td>
            <td class="px-4 py-3.5 font-mono text-xs text-gray-500">{{ campaign.trigger }}</td>
            <td class="px-4 py-3.5 text-xs text-gray-500">{{ campaign.config.period }}</td>
            <td class="px-4 py-3.5 text-xs text-gray-500">{{ campaign.config.target_periods }}</td>
            <td class="px-4 py-3.5">
              <app-status-pill [tone]="campaign.status === 'active' ? 'success' : 'neutral'" [dot]="true">
                {{ campaign.status }}
              </app-status-pill>
            </td>
            <td class="px-4 py-3.5 text-right">
              <app-icon-button
                [ariaLabel]="campaign.status === 'active' ? 'Disable campaign' : 'Activate campaign'"
                (click)="toggleStatus(campaign)"
              >
                {{ campaign.status === 'active' ? '⏸' : '▶' }}
              </app-icon-button>
              <app-icon-button ariaLabel="Delete campaign" (click)="remove(campaign)">🗑</app-icon-button>
            </td>
          </ng-template>
        </app-data-table>
        <div class="px-4">
          <app-paginator [page]="page()" [pageSize]="pageSize" [total]="total()" (pageChange)="loadPage($event)" />
        </div>
      </div>
    </div>
  `,
})
export class StreakCampaignsListPage implements OnInit {
  readonly programId = input.required<string>();

  private readonly campaignsService = inject(StreakCampaignsService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly programContext = inject(ProgramContextStore);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly campaigns = signal<readonly StreakCampaign[]>([]);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly trackById = (c: StreakCampaign): string => c.id;
  protected readonly programName = (): string => this.programContext.program()?.name ?? 'Program';

  protected readonly columns: Column<StreakCampaign>[] = [
    { key: 'name', header: 'Name' },
    { key: 'trigger', header: 'Trigger' },
    { key: 'period', header: 'Period' },
    { key: 'targetPeriods', header: 'Target periods' },
    { key: 'status', header: 'Status' },
    { key: 'actions', header: '' },
  ];

  ngOnInit(): void {
    void this.loadPage(1);
  }

  protected async loadPage(page: number): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.campaignsService.list(this.programId(), page, this.pageSize);
      this.campaigns.set(result.data);
      this.page.set(result.page);
      this.total.set(result.total);
    } finally {
      this.loading.set(false);
    }
  }

  protected create(): void {
    void this.router.navigate(['/programs', this.programId(), 'streak-campaigns', 'new']);
  }

  protected async toggleStatus(campaign: StreakCampaign): Promise<void> {
    const next = campaign.status === 'active' ? 'disabled' : 'active';
    await this.campaignsService.setStatus(this.programId(), campaign.id, next);
    this.toast.success(`Campaign ${next === 'active' ? 'activated' : 'disabled'}`);
    await this.loadPage(this.page());
  }

  protected async remove(campaign: StreakCampaign): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Delete campaign',
      message: `Delete "${campaign.name}"? This cannot be undone.`,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    await this.campaignsService.delete(this.programId(), campaign.id);
    this.toast.success('Campaign deleted');
    await this.loadPage(this.page());
  }
}
