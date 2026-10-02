import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ProgramOverviewPage } from './program-overview.page';
import { ProgramsService } from './programs.service';
import { AccountTypesService } from './account-types/account-types.service';
import { ConfirmService } from '../../core/ui/confirm.service';
import { ToastService } from '../../core/ui/toast.service';
import { Program } from './program.model';

// 1.3.CL items 7–8: the Active switch only works once published, and Publish is offered for a
// draft or when there are unpublished changes — never for an up-to-date published program.
describe('ProgramOverviewPage', () => {
  const base: Program = {
    id: 'p1',
    name: 'Stars',
    description: null,
    status: 'inactive',
    qualifyingAccountTypeId: null,
    warningDays: null,
    createdAt: '2026-09-01T00:00:00Z',
    accountTypeCount: 1,
    ruleCount: 0,
    publicationStatus: 'draft',
    hasUnpublishedChanges: false,
    publishedVersion: null,
    publishedAt: null,
    publishedBy: null,
    slug: 'stars',
  };

  async function setup(program: Program, confirmed = true) {
    const programs = {
      get: vi.fn().mockResolvedValue(program),
      update: vi
        .fn()
        .mockImplementation((_id: string, req: Partial<Program>) =>
          Promise.resolve({ ...program, ...req }),
        ),
      publish: vi.fn().mockResolvedValue({
        ...program,
        publicationStatus: 'published',
        hasUnpublishedChanges: false,
        publishedVersion: (program.publishedVersion ?? 0) + 1,
      }),
    };
    TestBed.configureTestingModule({
      imports: [ProgramOverviewPage],
      providers: [
        provideRouter([]),
        provideTranslateService(),
        { provide: ProgramsService, useValue: programs },
        { provide: AccountTypesService, useValue: { listAll: vi.fn().mockResolvedValue([]) } },
        { provide: ConfirmService, useValue: { ask: vi.fn().mockResolvedValue(confirmed) } },
        { provide: ToastService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(ProgramOverviewPage);
    fixture.componentRef.setInput('programId', program.id);
    fixture.detectChanges();
    await fixture.whenStable();
    const page = fixture.componentInstance;
    return {
      programs,
      canPublish: () => page['canPublish'](),
      program: () => page['program'](),
      toggleStatus: () => page['toggleStatus'](),
      publish: () => page['publish'](),
    };
  }

  it('offers Publish for a draft and does not let it be activated', async () => {
    const { canPublish, toggleStatus, programs } = await setup(base);
    expect(canPublish()).toBe(true);
    await toggleStatus();
    expect(programs.update).not.toHaveBeenCalled();
  });

  it('publishes after confirmation and stores the new version', async () => {
    const { publish, programs, program, canPublish } = await setup(base);
    await publish();
    expect(programs.publish).toHaveBeenCalledWith('p1');
    expect(program()?.publicationStatus).toBe('published');
    expect(program()?.publishedVersion).toBe(1);
    expect(canPublish()).toBe(false);
  });

  it('does not publish when the confirmation is cancelled', async () => {
    const { publish, programs } = await setup(base, false);
    await publish();
    expect(programs.publish).not.toHaveBeenCalled();
  });

  it('does not offer Publish for an up-to-date published program', async () => {
    const { canPublish } = await setup({
      ...base,
      publicationStatus: 'published',
      publishedVersion: 3,
    });
    expect(canPublish()).toBe(false);
  });

  it('offers Publish when a published program has unpublished changes', async () => {
    const { canPublish } = await setup({
      ...base,
      publicationStatus: 'published',
      publishedVersion: 3,
      hasUnpublishedChanges: true,
    });
    expect(canPublish()).toBe(true);
  });

  it('toggles a published program between active and inactive', async () => {
    const { toggleStatus, programs, program } = await setup({
      ...base,
      publicationStatus: 'published',
      publishedVersion: 1,
    });
    await toggleStatus();
    expect(programs.update).toHaveBeenCalledWith('p1', { status: 'active' });
    expect(program()?.status).toBe('active');
  });
});
