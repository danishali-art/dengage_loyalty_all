import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/http/api-client';
import { Page } from '../../core/http/pagination';
import { CreateProgramRequest, Program, UpdateProgramRequest } from './program.model';

@Injectable({ providedIn: 'root' })
export class ProgramsService {
  private readonly api = inject(ApiClient);

  list(page: number, pageSize: number): Promise<Page<Program>> {
    return firstValueFrom(this.api.tenantScope.getPage<Program>('programs', { page, pageSize }));
  }

  get(programId: string): Promise<Program> {
    return firstValueFrom(this.api.tenantScope.get<Program>(`programs/${programId}`));
  }

  create(request: CreateProgramRequest): Promise<Program> {
    return firstValueFrom(
      this.api.tenantScope.post<Program>('programs', request, { skipErrorToast: true }),
    );
  }

  update(programId: string, request: UpdateProgramRequest): Promise<Program> {
    return firstValueFrom(
      this.api.tenantScope.patch<Program>(`programs/${programId}`, request, {
        skipErrorToast: true,
      }),
    );
  }

  /** 1.3.CL items 8/9: Draft → Published, recording one ProgramPublication history version. */
  publish(programId: string): Promise<Program> {
    return firstValueFrom(
      this.api.tenantScope.post<Program>(`programs/${programId}/publish`, null, {
        skipErrorToast: true,
      }),
    );
  }

  delete(programId: string): Promise<void> {
    return firstValueFrom(
      this.api.tenantScope.delete<void>(`programs/${programId}`, { skipErrorToast: true }),
    );
  }
}
