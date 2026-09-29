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
import { CardBucketsService } from './card-buckets.service';
import { CardBucket } from './card-bucket.model';

const PAGE_SIZE = 20;

@Component({
  selector: 'app-card-buckets-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, DataTable, StatusPill, Button, IconButton, Paginator],
  template: `
    <app-page-header
      heading="Card Buckets"
      [crumbs]="[
        { label: 'Programs', link: ['/programs'] },
        { label: programName(), link: ['/programs', programId()] },
        { label: 'Card Buckets' },
      ]"
    >
      <div actions>
        <app-button (click)="create()">
          <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true"><path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" /></svg>
          New bucket
        </app-button>
      </div>
    </app-page-header>

    <div class="mx-auto max-w-5xl px-8 py-6">
      <div class="card !p-0">
        <app-data-table
          [columns]="columns"
          [rows]="buckets()"
          caption="Card Buckets"
          [busy]="loading()"
          emptyText="No card buckets yet — create one to reward specific spend patterns."
          [trackKey]="trackById"
        >
          <ng-template #row let-bucket>
            <td class="cursor-pointer px-4 py-3.5 font-medium text-gray-800" [routerLink]="['/programs', programId(), 'card-buckets', bucket.id]">
              {{ bucket.name }}
            </td>
            <td class="px-4 py-3.5 font-mono text-xs text-gray-500">{{ bucket.mccCodes?.join(', ') || '—' }}</td>
            <td class="px-4 py-3.5 text-xs text-gray-500">{{ amountRange(bucket) }}</td>
            <td class="px-4 py-3.5">
              <app-status-pill [tone]="bucket.status === 'active' ? 'success' : 'neutral'" [dot]="true">
                {{ bucket.status }}
              </app-status-pill>
            </td>
            <td class="px-4 py-3.5 text-right">
              <app-icon-button
                [ariaLabel]="bucket.status === 'active' ? 'Disable bucket' : 'Activate bucket'"
                (click)="toggleStatus(bucket)"
              >
                {{ bucket.status === 'active' ? '⏸' : '▶' }}
              </app-icon-button>
              <app-icon-button ariaLabel="Delete bucket" (click)="remove(bucket)">🗑</app-icon-button>
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
export class CardBucketsListPage implements OnInit {
  readonly programId = input.required<string>();

  private readonly bucketsService = inject(CardBucketsService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly programContext = inject(ProgramContextStore);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly buckets = signal<readonly CardBucket[]>([]);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly trackById = (b: CardBucket): string => b.id;
  protected readonly programName = (): string => this.programContext.program()?.name ?? 'Program';

  protected readonly columns: Column<CardBucket>[] = [
    { key: 'name', header: 'Name' },
    { key: 'mcc', header: 'MCC' },
    { key: 'amount', header: 'Amount range' },
    { key: 'status', header: 'Status' },
    { key: 'actions', header: '' },
  ];

  ngOnInit(): void {
    void this.loadPage(1);
  }

  protected async loadPage(page: number): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.bucketsService.list(this.programId(), page, this.pageSize);
      this.buckets.set(result.data);
      this.page.set(result.page);
      this.total.set(result.total);
    } finally {
      this.loading.set(false);
    }
  }

  protected amountRange(bucket: CardBucket): string {
    if (!bucket.amountMin && !bucket.amountMax) return '—';
    return `${bucket.amountMin ?? '0'}–${bucket.amountMax ?? '∞'}`;
  }

  protected create(): void {
    void this.router.navigate(['/programs', this.programId(), 'card-buckets', 'new']);
  }

  protected async toggleStatus(bucket: CardBucket): Promise<void> {
    const next = bucket.status === 'active' ? 'disabled' : 'active';
    await this.bucketsService.setStatus(this.programId(), bucket.id, next);
    this.toast.success(`Bucket ${next === 'active' ? 'activated' : 'disabled'}`);
    await this.loadPage(this.page());
  }

  protected async remove(bucket: CardBucket): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Delete bucket',
      message: `Delete "${bucket.name}"? This cannot be undone.`,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    await this.bucketsService.delete(this.programId(), bucket.id);
    this.toast.success('Bucket deleted');
    await this.loadPage(this.page());
  }
}
