import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { PageHeader } from '../../../shared/ui/page-header';
import { Button } from '../../../shared/ui/button';
import { EmptyState } from '../../../shared/ui/empty-state';
import { Paginator } from '../../../shared/ui/paginator';
import { DialogService } from '../../../core/ui/dialog.service';
import { ToastService } from '../../../core/ui/toast.service';
import { ProgramContextStore } from '../../../core/program/program-context.store';
import { AccountTypesService } from './account-types.service';
import { AccountType } from './account-type.model';
import { AccountTypeFormDialog, AccountTypeFormData } from './account-type-form.dialog';

const PAGE_SIZE = 12;

@Component({
  selector: 'app-account-types-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, PageHeader, Button, EmptyState, Paginator],
  template: `
    <app-page-header
      heading="Account Types"
      [crumbs]="[
        { label: 'Programs', link: ['/programs'] },
        { label: programName(), link: ['/programs', programId()] },
        { label: 'Account Types' },
      ]"
    >
      <div actions>
        <app-button (click)="create()">
          <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true">
            <path
              d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z"
            />
          </svg>
          Add account type
        </app-button>
      </div>
    </app-page-header>

    <div class="mx-auto max-w-5xl px-8 py-6">
      <div class="card">
        @if (loading() && accountTypes().length === 0) {
          <div class="space-y-3" aria-busy="true">
            @for (i of [0, 1, 2]; track i) {
              <div class="rounded-xl border border-gray-200 p-4" aria-hidden="true">
                <div class="flex items-center gap-3">
                  <div class="h-9 w-9 flex-shrink-0 animate-pulse rounded-lg bg-gray-200"></div>
                  <div class="flex items-center gap-2">
                    <div class="h-4 w-28 animate-pulse rounded bg-gray-200"></div>
                    <div class="h-5 w-14 animate-pulse rounded-md bg-gray-200"></div>
                  </div>
                </div>
              </div>
            }
          </div>
        } @else if (!loading() && accountTypes().length === 0) {
          <app-empty-state
            heading="No account types yet"
            description="Create one so this program has something to earn."
            icon="⭐"
          />
        } @else {
          <div class="space-y-3" [attr.aria-busy]="loading() ? 'true' : null">
            @for (at of accountTypes(); track at.id) {
              <div
                class="hover:border-brand rounded-xl border border-gray-200 p-4 transition-colors"
              >
                <div class="flex items-start justify-between">
                  <div class="flex items-center gap-3">
                    <div
                      class="bg-brand-light flex h-9 w-9 items-center justify-center rounded-lg text-lg"
                    >
                      {{ at.type === 'POINTS' ? '⭐' : at.type === 'CASH' ? '💳' : '🎟' }}
                    </div>
                    <div>
                      <div class="flex items-center gap-2">
                        <span class="text-sm font-medium text-gray-800">{{ at.name }}</span>
                        <span
                          class="rounded-md bg-gray-100 px-2.5 py-1 text-xs font-medium text-gray-600"
                          >{{ at.type }}</span
                        >
                        @if (at.isTierQualifying) {
                          <span
                            class="bg-brand-light text-brand rounded-md px-2.5 py-1 text-xs font-medium"
                          >
                            {{ 'accountTypes.tierQualifying.badge' | translate }}
                          </span>
                        }
                      </div>
                    </div>
                  </div>
                  <button
                    type="button"
                    class="hover:text-brand cursor-pointer rounded px-2 py-1 text-xs text-gray-400 transition-colors hover:bg-gray-50"
                    (click)="edit(at)"
                  >
                    Edit
                  </button>
                </div>
              </div>
            }
          </div>
          <app-paginator
            [page]="page()"
            [pageSize]="pageSize"
            [total]="total()"
            (pageChange)="loadPage($event)"
          />
        }
      </div>
    </div>
  `,
})
export class AccountTypesListPage implements OnInit {
  readonly programId = input.required<string>();

  private readonly accountTypesService = inject(AccountTypesService);
  private readonly dialog = inject(DialogService);
  private readonly toast = inject(ToastService);
  private readonly programContext = inject(ProgramContextStore);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly accountTypes = signal<readonly AccountType[]>([]);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly programName = (): string => this.programContext.program()?.name ?? 'Program';

  ngOnInit(): void {
    void this.loadPage(1);
  }

  protected async loadPage(page: number): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.accountTypesService.list(this.programId(), page, this.pageSize);
      this.accountTypes.set(result.data);
      this.page.set(result.page);
      this.total.set(result.total);
    } finally {
      this.loading.set(false);
    }
  }

  protected create(): void {
    const data: AccountTypeFormData = {
      programId: this.programId(),
      existingAccountTypes: this.accountTypes(),
    };
    const ref = this.dialog.open<AccountType, AccountTypeFormData, AccountTypeFormDialog>(
      AccountTypeFormDialog,
      { data },
    );
    ref.closed.subscribe((result) => {
      if (!result) return;
      this.toast.success(`Account type "${result.name}" created`);
      void this.loadPage(1);
    });
  }

  protected edit(existing: AccountType): void {
    const data: AccountTypeFormData = {
      programId: this.programId(),
      existing,
      existingAccountTypes: this.accountTypes(),
    };
    const ref = this.dialog.open<AccountType, AccountTypeFormData, AccountTypeFormDialog>(
      AccountTypeFormDialog,
      { data },
    );
    ref.closed.subscribe((result) => {
      if (!result) return;
      this.toast.success('Account type saved');
      void this.loadPage(this.page());
    });
  }
}
