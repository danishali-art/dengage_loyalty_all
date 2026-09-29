import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { TenantStore } from '../../core/tenant/tenant.store';
import { Tenant } from '../../core/tenant/tenant.model';
import { Paginator } from '../../shared/ui/paginator';
import { SkeletonList } from '../../shared/ui/skeleton';
import { EmptyState } from '../../shared/ui/empty-state';

const SEARCH_DEBOUNCE_MS = 300;

/** Interstitial for a platform_admin who has not yet picked a tenant to administer. */
@Component({
  selector: 'app-select-tenant-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, SkeletonList, EmptyState, Paginator],
  template: `
    <div class="mx-auto flex min-h-screen max-w-lg flex-col justify-center px-6 py-12">
      <h1 class="text-lg font-bold text-gray-900">{{ 'tenant.select.title' | translate }}</h1>
      <p class="mt-1 text-sm text-gray-500">{{ 'tenant.select.body' | translate }}</p>

      <input
        class="field-input mt-4"
        type="search"
        placeholder="Search by name or tenant id…"
        [value]="query()"
        (input)="onSearchInput($any($event.target).value)"
        autocomplete="off"
      />

      <div class="mt-4">
        @if (store.pickerLoading() && store.pickerResults().length === 0) {
          <app-skeleton-list [count]="3" />
        } @else if (!store.pickerLoading() && store.pickerResults().length === 0) {
          <app-empty-state heading="No tenants found" description="Try a different search." icon="🏢" />
        } @else {
          <ul class="space-y-2">
            @for (tenant of store.pickerResults(); track tenant.id) {
              <li>
                <button
                  type="button"
                  class="card flex w-full cursor-pointer items-center justify-between text-left transition-colors hover:border-brand"
                  (click)="pick(tenant)"
                >
                  <span class="text-sm font-medium text-gray-800">{{ tenant.name }}</span>
                  <span class="font-mono text-xs text-gray-500">{{ tenant.id }}</span>
                </button>
              </li>
            }
          </ul>
          <app-paginator
            [page]="store.pickerPage()"
            [pageSize]="store.pickerPageSize"
            [total]="store.pickerTotal()"
            (pageChange)="loadPage($event)"
          />
        }
      </div>
    </div>
  `,
})
export class SelectTenantPage implements OnInit {
  protected readonly store = inject(TenantStore);
  private searchDebounce?: ReturnType<typeof setTimeout>;

  protected readonly query = signal('');

  ngOnInit(): void {
    void this.store.searchTenants('');
  }

  protected onSearchInput(value: string): void {
    this.query.set(value);
    clearTimeout(this.searchDebounce);
    this.searchDebounce = setTimeout(() => void this.store.searchTenants(value, 1), SEARCH_DEBOUNCE_MS);
  }

  protected loadPage(page: number): void {
    void this.store.searchTenants(this.query(), page);
  }

  protected pick(tenant: Tenant): void {
    void this.store.setTenant(tenant.id, tenant);
  }
}
