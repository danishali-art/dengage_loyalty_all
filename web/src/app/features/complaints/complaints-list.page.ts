import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { PageHeader } from '../../shared/ui/page-header';
import { Button } from '../../shared/ui/button';
import { DataTable, Column } from '../../shared/ui/data-table';
import { Paginator } from '../../shared/ui/paginator';
import { StatusPill, StatusTone } from '../../shared/ui/status-pill';
import { FormErrors } from '../../shared/ui/form-errors';
import { ToastService } from '../../core/ui/toast.service';
import { ApiError } from '../../core/http/api-error';
import { emptyPage } from '../../core/http/pagination';
import { ComplaintsService } from './complaints.service';
import { Complaint, ComplaintStatus } from './complaint.model';

const PAGE_SIZE = 20;
const STATUS_TONE: Record<ComplaintStatus, StatusTone> = { open: 'danger', in_progress: 'warning', resolved: 'success' };
const STATUS_LABEL: Record<ComplaintStatus, string> = { open: 'Open', in_progress: 'In progress', resolved: 'Resolved' };

@Component({
  selector: 'app-complaints-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, DatePipe, PageHeader, Button, DataTable, Paginator, StatusPill, FormErrors],
  template: `
    <app-page-header heading="Complaints" subtitle="Customer complaints across every program." />

    <div class="mx-auto max-w-5xl px-8 py-6 space-y-6">
      <div class="card">
        <div class="section-header">
          <h2 class="section-heading">New complaint</h2>
        </div>
        <app-form-errors [messages]="formErrors()" />
        <form [formGroup]="form" (ngSubmit)="submit()" class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-[1fr_1fr_auto]">
          <input formControlName="subject" placeholder="Subject" class="field-input" />
          <input formControlName="customerKey" placeholder="Customer key (optional)" class="field-input" />
          <app-button type="submit" [pending]="creating()">Add</app-button>
          <textarea
            formControlName="description"
            placeholder="Description (optional)"
            rows="2"
            class="field-input resize-none sm:col-span-3"
          ></textarea>
        </form>
      </div>

      <div class="card !p-0">
        <app-data-table
          [columns]="columns"
          [rows]="page().data"
          caption="Complaints"
          [busy]="loading()"
          emptyText="No complaints yet"
          [trackKey]="trackById"
        >
          <ng-template #row let-c>
            <td class="px-4 py-3.5">{{ c.subject }}</td>
            <td class="px-4 py-3.5 text-xs text-gray-500">{{ c.customerKey ?? '—' }}</td>
            <td class="px-4 py-3.5 text-xs text-gray-500">{{ c.createdAt | date: 'short' }}</td>
            <td class="px-4 py-3.5">
              <app-status-pill [tone]="tone(c.status)" [dot]="true">{{ label(c.status) }}</app-status-pill>
            </td>
            <td class="px-4 py-3.5 text-right">
              <select
                class="field-input !w-auto text-xs"
                [value]="c.status"
                (change)="changeStatus(c, $any($event.target).value)"
              >
                <option value="open">Open</option>
                <option value="in_progress">In progress</option>
                <option value="resolved">Resolved</option>
              </select>
            </td>
          </ng-template>
        </app-data-table>
        <app-paginator [page]="page().page" [pageSize]="page().pageSize" [total]="page().total" (pageChange)="goToPage($event)" />
      </div>
    </div>
  `,
})
export class ComplaintsListPage implements OnInit {
  private readonly complaintsService = inject(ComplaintsService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly page = signal(emptyPage<Complaint>(PAGE_SIZE));
  protected readonly loading = signal(true);
  protected readonly creating = signal(false);
  protected readonly formErrors = signal<string[]>([]);
  protected readonly trackById = (c: Complaint): string => c.id;

  protected readonly columns: Column<Complaint>[] = [
    { key: 'subject', header: 'Subject' },
    { key: 'customerKey', header: 'Customer' },
    { key: 'createdAt', header: 'Created' },
    { key: 'status', header: 'Status' },
    { key: 'actions', header: '' },
  ];

  protected readonly form = this.fb.group({
    subject: this.fb.control('', [Validators.required, Validators.maxLength(255)]),
    customerKey: this.fb.control(''),
    description: this.fb.control(''),
  });

  ngOnInit(): void {
    void this.reload(1);
  }

  private async reload(pageNumber: number): Promise<void> {
    this.loading.set(true);
    try {
      this.page.set(await this.complaintsService.list(pageNumber, PAGE_SIZE));
    } finally {
      this.loading.set(false);
    }
  }

  protected goToPage(pageNumber: number): void {
    void this.reload(pageNumber);
  }

  protected tone(status: ComplaintStatus): StatusTone {
    return STATUS_TONE[status];
  }

  protected label(status: ComplaintStatus): string {
    return STATUS_LABEL[status];
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.creating()) return;
    this.creating.set(true);
    this.formErrors.set([]);
    try {
      const v = this.form.getRawValue();
      await this.complaintsService.create({
        subject: v.subject,
        customerKey: v.customerKey || null,
        description: v.description || null,
      });
      this.form.reset({ subject: '', customerKey: '', description: '' });
      this.toast.success('Complaint added');
      await this.reload(1);
    } catch (err) {
      if (err instanceof ApiError) this.formErrors.set([err.message]);
      else this.formErrors.set(['Something went wrong. Please try again.']);
    } finally {
      this.creating.set(false);
    }
  }

  protected async changeStatus(complaint: Complaint, status: ComplaintStatus): Promise<void> {
    try {
      await this.complaintsService.updateStatus(complaint.id, status);
      this.toast.success('Status updated');
      await this.reload(this.page().page);
    } catch (err) {
      if (err instanceof ApiError) this.toast.error(err.message);
      else this.toast.error('Something went wrong. Please try again.');
    }
  }
}
