import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { PageHeader } from '../../shared/ui/page-header';
import { DataTable, Column } from '../../shared/ui/data-table';
import { Paginator } from '../../shared/ui/paginator';
import { StatusPill } from '../../shared/ui/status-pill';
import { Button } from '../../shared/ui/button';
import { DialogService } from '../../core/ui/dialog.service';
import { ToastService } from '../../core/ui/toast.service';
import { ProgramsService } from './programs.service';
import { CreateProgramDialog } from './create-program.dialog';
import { Program } from './program.model';

const PAGE_SIZE = 25;

@Component({
  selector: 'app-programs-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, PageHeader, DataTable, Paginator, StatusPill, Button],
  template: `
    <app-page-header heading="Loyalty Programs">
      <div actions>
        <app-button (click)="openCreate()">
          <svg viewBox="0 0 20 20" fill="currentColor" class="h-4 w-4" aria-hidden="true">
            <path
              d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z"
            />
          </svg>
          New program
        </app-button>
      </div>
    </app-page-header>

    <div class="mx-auto max-w-7xl px-8 py-6">
      <div class="card !p-0">
        <app-data-table
          [columns]="columns"
          [rows]="rows()"
          caption="Programs"
          [busy]="loading()"
          emptyText="No programs yet — create one to get started."
          [trackKey]="trackById"
        >
          <ng-template #row let-program>
            <td
              class="cursor-pointer px-4 py-3.5 font-medium text-gray-800"
              (click)="open(program.id)"
            >
              {{ program.name }}
            </td>
            <td class="cursor-pointer px-4 py-3.5" (click)="open(program.id)">
              <app-status-pill
                [tone]="program.status === 'active' ? 'success' : 'neutral'"
                [dot]="true"
              >
                {{ program.status }}
              </app-status-pill>
              <!-- 1.3.CL item 8: a draft never runs, whatever its status says. -->
              @if (program.publicationStatus === 'draft') {
                <app-status-pill tone="warning">{{
                  'programs.publication.draftShort' | translate
                }}</app-status-pill>
              } @else if (program.hasUnpublishedChanges) {
                <app-status-pill tone="info">{{
                  'programs.publication.unpublishedChanges' | translate
                }}</app-status-pill>
              }
            </td>
            <td class="cursor-pointer px-4 py-3.5 text-gray-500" (click)="open(program.id)">
              {{ program.accountTypeCount }}
            </td>
            <td class="cursor-pointer px-4 py-3.5 text-gray-500" (click)="open(program.id)">
              {{ program.ruleCount }}
            </td>
          </ng-template>
        </app-data-table>
        <div class="px-4">
          <app-paginator
            [page]="page()"
            [pageSize]="pageSize"
            [total]="total()"
            (pageChange)="loadPage($event)"
          />
        </div>
      </div>
    </div>
  `,
})
export class ProgramsListPage implements OnInit {
  private readonly programsService = inject(ProgramsService);
  private readonly dialog = inject(DialogService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly rows = signal<readonly Program[]>([]);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly trackById = (p: Program): string => p.id;

  protected readonly columns: Column<Program>[] = [
    { key: 'name', header: 'Name' },
    { key: 'status', header: 'Status' },
    { key: 'accountTypeCount', header: 'Account Types' },
    { key: 'ruleCount', header: 'Rules' },
  ];

  ngOnInit(): void {
    void this.loadPage(1);
  }

  protected async loadPage(page: number): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.programsService.list(page, this.pageSize);
      this.rows.set(result.data);
      this.page.set(result.page);
      this.total.set(result.total);
    } finally {
      this.loading.set(false);
    }
  }

  protected open(programId: string): void {
    void this.router.navigate(['/programs', programId]);
  }

  protected openCreate(): void {
    const ref = this.dialog.open<Program, void, CreateProgramDialog>(CreateProgramDialog);
    ref.closed.subscribe((program) => {
      if (!program) return;
      this.toast.success(`Program "${program.name}" created`);
      void this.router.navigate(['/programs', program.id]);
    });
  }
}
