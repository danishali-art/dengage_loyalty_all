import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DialogRef } from '@angular/cdk/dialog';
import { closeDialogAnimated } from '../../core/ui/dialog.service';
import { DialogShell } from '../../shared/ui/dialog-shell';
import { Button } from '../../shared/ui/button';
import { Paginator } from '../../shared/ui/paginator';
import { EmptyState } from '../../shared/ui/empty-state';
import { TenantStore } from '../../core/tenant/tenant.store';
import { Tenant } from '../../core/tenant/tenant.model';

const SEARCH_DEBOUNCE_MS = 300;

/**
 * The tenant switcher's popup — a searchable, paginated list of every tenant, not just the
 * first `platformTenantPageSize` the switcher's own label cache holds.
 */
@Component({
  selector: 'app-tenant-picker-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DialogShell, Button, Paginator, EmptyState],
  template: `
    <app-dialog-shell heading="Switch tenant" (closed)="closeAnimated()">
      <div class="space-y-3">
        <input
          class="field-input"
          type="search"
          placeholder="Search by name or tenant id…"
          [value]="query()"
          (input)="onSearchInput($any($event.target).value)"
          autocomplete="off"
        />

        <div class="min-h-[16rem] max-h-96 overflow-y-auto rounded-lg border border-gray-200">
          @if (store.pickerLoading() && store.pickerResults().length === 0) {
            <div class="flex items-center justify-center gap-2 py-10 text-sm text-gray-500" aria-busy="true">
              <span
                class="h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent text-gray-400"
                aria-hidden="true"
              ></span>
              Loading…
            </div>
          } @else if (!store.pickerLoading() && store.pickerResults().length === 0) {
            <div class="p-5">
              <app-empty-state heading="No tenants found" description="Try a different search." icon="🏢" />
            </div>
          } @else {
            <ul role="listbox" aria-label="Tenants">
              @for (tenant of store.pickerResults(); track tenant.id) {
                <li
                  role="option"
                  [attr.aria-selected]="tenant.id === store.activeTenantId()"
                  class="flex items-center justify-between gap-3 border-b border-gray-100 px-3 py-2 last:border-b-0 hover:bg-gray-50"
                >
                  <div class="min-w-0">
                    <div class="truncate text-sm font-medium text-gray-800">{{ tenant.name }}</div>
                    <div class="truncate font-mono text-xs text-gray-500">{{ tenant.id }}</div>
                  </div>
                  @if (tenant.id === store.activeTenantId()) {
                    <span class="shrink-0 rounded-md bg-brand-light px-2 py-1 text-xs font-medium text-brand">Current</span>
                  } @else {
                    <app-button size="sm" variant="secondary" (click)="select(tenant)">Select</app-button>
                  }
                </li>
              }
            </ul>
          }
        </div>

        <app-paginator
          [page]="store.pickerPage()"
          [pageSize]="store.pickerPageSize"
          [total]="store.pickerTotal()"
          (pageChange)="loadPage($event)"
        />
      </div>

      <app-button footer variant="secondary" (click)="closeAnimated()">Close</app-button>
    </app-dialog-shell>
  `,
})
export class TenantPickerDialog implements OnInit {
  readonly ref = inject<DialogRef<void, TenantPickerDialog>>(DialogRef);
  protected readonly store = inject(TenantStore);
  private searchDebounce?: ReturnType<typeof setTimeout>;

  protected readonly query = signal('');

  ngOnInit(): void {
    void this.store.searchTenants('');
  }

  protected closeAnimated(): void {
    closeDialogAnimated(this.ref);
  }

  protected onSearchInput(value: string): void {
    this.query.set(value);
    clearTimeout(this.searchDebounce);
    this.searchDebounce = setTimeout(() => void this.store.searchTenants(value, 1), SEARCH_DEBOUNCE_MS);
  }

  protected loadPage(page: number): void {
    void this.store.searchTenants(this.query(), page);
  }

  protected select(tenant: Tenant): void {
    this.closeAnimated();
    void this.store.setTenant(tenant.id, tenant);
  }
}
