/**
 * The programs vertical slice end to end, with the API layer as the only seam: the entity's
 * request functions are mocked, everything above them (queries, composables, wrappers) is real.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { mountWithProviders } from '../../../../tests/utils/mount-with-providers';
import { useTenantContextStore } from '@/shared/model';
import * as programApi from '@/entities/program/api/program.api';
import type { Program } from '@/entities/program';
import ProgramsListPage from './ProgramsListPage.vue';

vi.mock('@/entities/program/api/program.api');

function program(overrides: Partial<Program> = {}): Program {
  return {
    id: 'p1',
    name: 'NovaPay Rewards',
    status: 'active',
    publicationStatus: 'published',
    hasUnpublishedChanges: true,
    accountTypeCount: 2,
    ruleCount: 8,
    createdAt: '2026-01-01T00:00:00Z',
    slug: 'novapay',
    defaultRounding: 'down',
    publishedVersion: 2,
    ...overrides,
  } as Program;
}

describe('ProgramsListPage', () => {
  beforeEach(() => {
    vi.mocked(programApi.listPrograms).mockResolvedValue({
      data: [
        program(),
        program({
          id: 'p2',
          name: 'Summer 2026',
          status: 'inactive',
          publicationStatus: 'draft',
          hasUnpublishedChanges: false,
        }),
      ],
      page: 1,
      pageSize: 25,
      total: 2,
    });
  });

  afterEach(() => {
    vi.clearAllMocks();
    document.body.innerHTML = '';
  });

  async function mountPage() {
    const pinia = createPinia();
    const mounted = await mountWithProviders(ProgramsListPage, { pinia, initialPath: '/programs' });
    useTenantContextStore(pinia).set('novapay');
    await flushPromises();
    return mounted;
  }

  it("lists the active tenant's programs with their publication state", async () => {
    const { wrapper } = await mountPage();
    expect(programApi.listPrograms).toHaveBeenCalledWith('novapay', 1, 25);
    const text = wrapper.text();
    expect(text).toContain('NovaPay Rewards');
    expect(text).toContain('Unpublished changes');
    expect(text).toContain('Summer 2026');
    expect(text).toContain('Draft');
    expect(text).toContain('1–2 of 2');
  });

  it('creates a program from the dialog and opens it', async () => {
    vi.mocked(programApi.createProgram).mockResolvedValue(program({ id: 'p9', name: 'Fresh' }));
    const { wrapper, router } = await mountPage();

    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'New program')!
      .trigger('click');
    await flushPromises();
    const name = document.querySelector<HTMLInputElement>('#program-name')!;
    name.value = 'Fresh';
    name.dispatchEvent(new Event('input'));
    await flushPromises();
    [...document.querySelectorAll('button')].find((b) => b.textContent?.includes('Create program'))!.click();

    // VeeValidate validates asynchronously before the submit handler runs.
    await vi.waitFor(() =>
      expect(programApi.createProgram).toHaveBeenCalledWith('novapay', {
        name: 'Fresh',
        description: null,
        slug: null,
      }),
    );
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/programs/p9'));
  });

  it('shows field errors from the server on the form', async () => {
    const { apiErrorFrom } = await import('@/shared/api');
    vi.mocked(programApi.createProgram).mockRejectedValue(
      apiErrorFrom(409, {
        title: 'Conflict',
        code: 'slug_already_exists',
        errors: { slug: ['Slug already in use.'] },
      }),
    );
    const { wrapper } = await mountPage();
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'New program')!
      .trigger('click');
    await flushPromises();
    for (const [id, value] of [
      ['#program-name', 'Dup'],
      ['#program-slug', 'taken'],
    ] as const) {
      const input = document.querySelector<HTMLInputElement>(id)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    await flushPromises();
    [...document.querySelectorAll('button')].find((b) => b.textContent?.includes('Create program'))!.click();
    await vi.waitFor(() => expect(document.body.textContent).toContain('Slug already in use.'));
  });
});
