import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../../core/http/api-client';
import { Page } from '../../../core/http/pagination';
import {
  CreateRuleRequest,
  Rule,
  RuleListFilter,
  RulesMetadata,
  UpdateRuleRequest,
} from './rule.model';

/** The API's page-size cap (PageRequest clamps to 100). */
const ALL_PAGE_SIZE = 100;

@Injectable({ providedIn: 'root' })
export class RulesService {
  private readonly api = inject(ApiClient);

  list(
    programId: string,
    page = 1,
    pageSize = 20,
    filter: RuleListFilter = {},
  ): Promise<Page<Rule>> {
    return firstValueFrom(
      this.api.tenantScope.getPage<Rule>(`programs/${programId}/rules`, {
        page,
        pageSize,
        ...(filter as Record<string, unknown>),
      }),
    );
  }

  /**
   * Every (non-deleted) rule of a program — the grouped rules list needs the whole set, since a
   * trigger/account group split across pages would misrepresent which rules compete. Pages
   * through the API's 100-row cap rather than assuming a program stays under it.
   */
  async listAll(programId: string): Promise<Rule[]> {
    const all: Rule[] = [];
    for (let page = 1; ; page++) {
      const result = await this.list(programId, page, ALL_PAGE_SIZE);
      all.push(...result.data);
      if (result.data.length === 0 || all.length >= result.total) return all;
    }
  }

  get(programId: string, ruleId: string): Promise<Rule> {
    return firstValueFrom(this.api.tenantScope.get<Rule>(`programs/${programId}/rules/${ruleId}`));
  }

  // CR-03: single compatibility source of truth — served by the engine's RuleTypeCatalog, not
  // hardcoded option lists in the form.
  getMetadata(programId: string): Promise<RulesMetadata> {
    return firstValueFrom(
      this.api.tenantScope.get<RulesMetadata>(`programs/${programId}/rules/metadata`),
    );
  }

  create(programId: string, request: CreateRuleRequest): Promise<Rule> {
    return firstValueFrom(
      this.api.tenantScope.post<Rule>(`programs/${programId}/rules`, request, {
        skipErrorToast: true,
      }),
    );
  }

  update(programId: string, ruleId: string, request: UpdateRuleRequest): Promise<Rule> {
    return firstValueFrom(
      this.api.tenantScope.patch<Rule>(`programs/${programId}/rules/${ruleId}`, request, {
        skipErrorToast: true,
      }),
    );
  }

  setStatus(programId: string, ruleId: string, status: 'active' | 'disabled'): Promise<Rule> {
    return firstValueFrom(
      this.api.tenantScope.patch<Rule>(`programs/${programId}/rules/${ruleId}/status`, { status }),
    );
  }

  // CR-04 CASH approval gate: creator cannot self-approve (enforced server-side).
  approve(programId: string, ruleId: string): Promise<Rule> {
    return firstValueFrom(
      this.api.tenantScope.patch<Rule>(
        `programs/${programId}/rules/${ruleId}/approve`,
        {},
        {
          skipErrorToast: true,
        },
      ),
    );
  }

  delete(programId: string, ruleId: string): Promise<void> {
    return firstValueFrom(
      this.api.tenantScope.delete<void>(`programs/${programId}/rules/${ruleId}`),
    );
  }
}
