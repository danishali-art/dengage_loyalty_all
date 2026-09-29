import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { PageHeader } from '../../shared/ui/page-header';
import { Button } from '../../shared/ui/button';
import { DataTable, Column } from '../../shared/ui/data-table';
import { Paginator } from '../../shared/ui/paginator';
import { EmptyState } from '../../shared/ui/empty-state';
import { CustomersService } from './customers.service';
import { CustomerSummary } from './customer.model';

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 350;

@Component({
  selector: 'app-customers-search-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, Button, DataTable, Paginator, EmptyState, DatePipe],
  template: `
    <app-page-header heading="Customers" subtitle="Browse every customer, or jump straight to one you already know." />

    <div class="mx-auto max-w-5xl space-y-6 px-8 py-6">
      <!-- Quick lookup by an exact contact key -->
      <form class="card flex items-end gap-3" (ngSubmit)="lookup()">
        <div class="flex-1">
          <label for="contactKey" class="field-label">Jump to a contact key</label>
          <input
            id="contactKey"
            class="field-input"
            placeholder="e.g. a customer's phone, email, or account id"
            [value]="contactKey()"
            (input)="contactKey.set($any($event.target).value)"
            autocomplete="off"
          />
        </div>
        <app-button type="submit" [disabled]="!contactKey().trim()">View profile</app-button>
      </form>

      <!-- Browsable, searchable, paginated grid -->
      <div class="card !p-0">
        <div class="flex items-center justify-between rounded-t-xl border-b border-gray-100 bg-brand-light p-4">
          <h2 class="section-heading">All customers</h2>
          <input
            class="field-input max-w-xs"
            placeholder="Filter by contact key…"
            [value]="search()"
            (input)="onSearchInput($any($event.target).value)"
            autocomplete="off"
          />
        </div>
        @if (!loading() && customers().length === 0) {
          <div class="p-5">
            <app-empty-state
              heading="No customers yet"
              description="Customers appear here once an ingested event creates their first balance."
              icon="👥"
            />
          </div>
        } @else {
          <app-data-table
            [columns]="columns"
            [rows]="customers()"
            caption="Customers"
            [busy]="loading()"
            [trackKey]="trackByKey"
          >
            <ng-template #row let-c>
              <td class="cursor-pointer px-4 py-3.5 font-mono text-sm font-medium text-gray-800" (click)="open(c.contactKey)">
                {{ c.contactKey }}
              </td>
              <td class="cursor-pointer px-4 py-3.5 text-gray-500" (click)="open(c.contactKey)">{{ c.accountCount }}</td>
              <td class="cursor-pointer px-4 py-3.5 text-gray-500" (click)="open(c.contactKey)">{{ c.lastActivityAt | date: 'medium' }}</td>
            </ng-template>
          </app-data-table>
          <div class="px-4">
            <app-paginator [page]="page()" [pageSize]="pageSize" [total]="total()" (pageChange)="loadPage($event)" />
          </div>
        }
      </div>
    </div>
  `,
})
export class CustomersSearchPage implements OnInit {
  private readonly router = inject(Router);
  private readonly customersService = inject(CustomersService);
  private searchDebounce?: ReturnType<typeof setTimeout>;

  protected readonly contactKey = signal('');
  protected readonly pageSize = PAGE_SIZE;
  protected readonly customers = signal<readonly CustomerSummary[]>([]);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly search = signal('');
  protected readonly trackByKey = (c: CustomerSummary): string => c.contactKey;

  protected readonly columns: Column<CustomerSummary>[] = [
    { key: 'contactKey', header: 'Contact key' },
    { key: 'accountCount', header: 'Accounts' },
    { key: 'lastActivityAt', header: 'Last activity' },
  ];

  ngOnInit(): void {
    void this.loadPage(1);
  }

  protected async loadPage(page: number): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.customersService.list(page, this.pageSize, this.search());
      this.customers.set(result.data);
      this.page.set(result.page);
      this.total.set(result.total);
    } finally {
      this.loading.set(false);
    }
  }

  protected onSearchInput(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchDebounce);
    this.searchDebounce = setTimeout(() => void this.loadPage(1), SEARCH_DEBOUNCE_MS);
  }

  protected open(contactKey: string): void {
    void this.router.navigate(['/customers', contactKey]);
  }

  protected lookup(): void {
    const key = this.contactKey().trim();
    if (!key) return;
    void this.router.navigate(['/customers', key]);
  }
}
