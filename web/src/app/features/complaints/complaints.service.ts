import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/http/api-client';
import { Page } from '../../core/http/pagination';
import { Complaint, ComplaintStatus, CreateComplaintRequest } from './complaint.model';

@Injectable({ providedIn: 'root' })
export class ComplaintsService {
  private readonly api = inject(ApiClient);

  list(page: number, pageSize: number, status?: ComplaintStatus | null): Promise<Page<Complaint>> {
    const params: Record<string, unknown> = {};
    if (status) params['status'] = status;
    return firstValueFrom(this.api.tenantScope.getPage<Complaint>('complaints', { page, pageSize, ...params }));
  }

  create(request: CreateComplaintRequest): Promise<Complaint> {
    return firstValueFrom(this.api.tenantScope.post<Complaint>('complaints', request, { skipErrorToast: true }));
  }

  updateStatus(complaintId: string, status: ComplaintStatus): Promise<Complaint> {
    return firstValueFrom(
      this.api.tenantScope.patch<Complaint>(`complaints/${complaintId}/status`, { status }, { skipErrorToast: true }),
    );
  }
}
