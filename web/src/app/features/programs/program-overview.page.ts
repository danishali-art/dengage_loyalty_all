import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { PageHeader } from '../../shared/ui/page-header';
import { Button } from '../../shared/ui/button';
import { FieldHint } from '../../shared/ui/field-hint';
import { StatusPill } from '../../shared/ui/status-pill';
import { ToastService } from '../../core/ui/toast.service';
import { ConfirmService } from '../../core/ui/confirm.service';
import { ApiError } from '../../core/http/api-error';
import { ProgramContextStore } from '../../core/program/program-context.store';
import { ProgramsService } from './programs.service';
import { PROGRAM_SLUG_PATTERN, Program } from './program.model';
import { AccountTypesService } from './account-types/account-types.service';
import { AccountType } from './account-types/account-type.model';

interface SummaryCard {
  /** i18n key segment: programs.overview.cards.<key>.label / .description */
  key: string;
  icon: string;
  link: string;
}

@Component({
  selector: 'app-program-overview-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
    PageHeader,
    Button,
    FieldHint,
    StatusPill,
  ],
  template: `
    <app-page-header
      [heading]="program()?.name ?? 'Program'"
      [crumbs]="[{ label: 'Programs', link: ['/programs'] }, { label: program()?.name ?? '' }]"
    >
      <div actions class="flex items-center gap-2">
        <!-- 1.3.CL item 8: Draft/Published lifecycle and the Publish action. -->
        @if (program(); as p) {
          <app-status-pill [tone]="p.publicationStatus === 'published' ? 'success' : 'warning'">
            {{
              (p.publicationStatus === 'published'
                ? 'programs.publication.published'
                : 'programs.publication.draft'
              ) | translate: { version: p.publishedVersion }
            }}
          </app-status-pill>
          @if (p.hasUnpublishedChanges) {
            <app-status-pill tone="info">{{
              'programs.publication.unpublishedChanges' | translate
            }}</app-status-pill>
          }
          <app-button
            [disabled]="!canPublish()"
            [pending]="publishing()"
            (click)="publish()"
            [attr.title]="canPublish() ? null : ('programs.publication.upToDate' | translate)"
          >
            {{
              (p.publicationStatus === 'draft'
                ? 'programs.publication.publish'
                : 'programs.publication.publishChanges'
              ) | translate
            }}
          </app-button>
        }
        <span
          [attr.title]="isRunning() ? 'Program must be inactive before it can be deleted.' : null"
        >
          <app-button
            variant="danger"
            [disabled]="isRunning()"
            [pending]="deleting()"
            (click)="deleteProgram()"
          >
            Delete
          </app-button>
        </span>
        <app-button variant="secondary" [pending]="savingInfo()" (click)="saveInfo()"
          >Save</app-button
        >
      </div>
    </app-page-header>

    <div class="mx-auto max-w-5xl px-8 py-6">
      @if (program(); as p) {
        <div class="grid grid-cols-5 gap-6">
          <!-- Program info -->
          <div class="col-span-2">
            <div class="card">
              <div class="section-header">
                <h2 class="section-heading">Program information</h2>
              </div>
              <form [formGroup]="infoForm" class="space-y-4">
                <div>
                  <label for="name" class="field-label">Program name</label>
                  <input id="name" formControlName="name" class="field-input" />
                </div>
                <!-- CR 2026-09-30 (A5): prefixes reward names; fixed once published. -->
                <div>
                  <label for="slug" class="field-label">{{
                    'programs.slug.label' | translate
                  }}</label>
                  <input
                    id="slug"
                    formControlName="slug"
                    class="field-input font-mono text-xs"
                    aria-describedby="slug-hint"
                    [readonly]="p.publicationStatus !== 'draft'"
                  />
                  <app-field-hint
                    id="slug-hint"
                    [hint]="
                      (p.publicationStatus === 'draft'
                        ? 'programs.slug.draftHint'
                        : 'programs.slug.locked'
                      ) | translate
                    "
                  />
                </div>
                <!-- 1.3.CL item 7: an Active/Inactive switch, saved immediately; only after publish. -->
                <div>
                  <span id="status-label" class="field-label">{{
                    'programs.status.label' | translate
                  }}</span>
                  <div class="flex items-center gap-3">
                    <button
                      type="button"
                      role="switch"
                      aria-labelledby="status-label"
                      aria-describedby="status-hint"
                      [attr.aria-checked]="isRunning()"
                      [disabled]="p.publicationStatus === 'draft' || togglingStatus()"
                      (click)="toggleStatus()"
                      class="relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full transition-colors disabled:cursor-not-allowed disabled:opacity-50"
                      [class.bg-brand]="isRunning()"
                      [class.bg-gray-300]="!isRunning()"
                    >
                      <span
                        aria-hidden="true"
                        class="inline-block h-5 w-5 translate-y-0.5 rounded-full bg-white shadow transition-transform"
                        [class.translate-x-5]="isRunning()"
                        [class.translate-x-0.5]="!isRunning()"
                      ></span>
                    </button>
                    <span class="text-sm text-gray-700">{{
                      (isRunning() ? 'common.active' : 'common.inactive') | translate
                    }}</span>
                  </div>
                  <app-field-hint
                    id="status-hint"
                    [hint]="
                      (p.publicationStatus === 'draft'
                        ? 'programs.status.draftHint'
                        : 'programs.status.hint'
                      ) | translate
                    "
                  />
                </div>
                <div>
                  <label for="description" class="field-label">Description</label>
                  <textarea
                    id="description"
                    formControlName="description"
                    rows="3"
                    class="field-input resize-none"
                  ></textarea>
                </div>
                <!-- 1.3.CL item 1: tier qualifying account and expiry warning moved to Account Types. -->
                <div>
                  <span class="field-label">{{
                    'accountTypes.tierQualifying.label' | translate
                  }}</span>
                  <p class="text-sm text-gray-700">
                    {{ qualifyingAccountName() ?? ('programs.tierQualifying.none' | translate) }}
                  </p>
                  <app-field-hint [hint]="'programs.tierQualifying.movedHint' | translate" />
                  <a
                    routerLink="account-types"
                    class="text-brand text-xs font-medium hover:underline"
                  >
                    {{ 'programs.tierQualifying.manage' | translate }}
                  </a>
                </div>
              </form>
            </div>
          </div>

          <!-- Sub-resource summary cards -->
          <div class="col-span-3 grid grid-cols-2 gap-4">
            @for (card of cards; track card.link) {
              <a [routerLink]="[card.link]" class="card hover:border-brand block transition-colors">
                <div class="flex items-start justify-between">
                  <div class="flex items-center gap-3">
                    <div
                      class="bg-brand-light flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg text-lg"
                    >
                      {{ card.icon }}
                    </div>
                    <div>
                      <div class="text-sm font-medium text-gray-800">
                        {{ 'programs.overview.cards.' + card.key + '.label' | translate }}
                      </div>
                      <div class="mt-0.5 text-xs text-gray-400">
                        {{ 'programs.overview.cards.' + card.key + '.description' | translate }}
                      </div>
                    </div>
                  </div>
                </div>
                <div class="text-brand mt-3 text-right text-sm font-medium">
                  {{ 'programs.overview.manage' | translate }}
                </div>
              </a>
            }
          </div>
        </div>
      }
    </div>
  `,
})
export class ProgramOverviewPage implements OnInit {
  readonly programId = input.required<string>();

  private readonly programsService = inject(ProgramsService);
  private readonly accountTypesService = inject(AccountTypesService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly programContext = inject(ProgramContextStore);
  private readonly translate = inject(TranslateService);

  protected readonly program = signal<Program | null>(null);
  protected readonly savingInfo = signal(false);
  protected readonly deleting = signal(false);
  protected readonly isRunning = (): boolean => this.program()?.status === 'active';
  protected readonly qualifyingAccountName = signal<string | null>(null);
  protected readonly publishing = signal(false);
  protected readonly togglingStatus = signal(false);
  /** A draft can always be published; a published program only when something changed since. */
  protected readonly canPublish = (): boolean => {
    const p = this.program();
    return !!p && (p.publicationStatus === 'draft' || p.hasUnpublishedChanges);
  };

  protected readonly cards: SummaryCard[] = [
    { key: 'accountTypes', icon: '⭐', link: 'account-types' },
    { key: 'tiers', icon: '🏅', link: 'tiers' },
    { key: 'rules', icon: '⚙', link: 'rules' },
    { key: 'rewards', icon: '🎁', link: 'rewards' },
    { key: 'streakCampaigns', icon: '🔥', link: 'streak-campaigns' },
    { key: 'cardBuckets', icon: '💳', link: 'card-buckets' },
    { key: 'history', icon: '🕓', link: 'history' },
  ];

  protected readonly infoForm = this.fb.group({
    name: this.fb.control('', [Validators.required, Validators.maxLength(255)]),
    description: this.fb.control(''),
    slug: this.fb.control('', [
      Validators.required,
      Validators.minLength(2),
      Validators.maxLength(40),
      Validators.pattern(PROGRAM_SLUG_PATTERN),
    ]),
  });

  ngOnInit(): void {
    void this.reloadProgram();
    void this.loadQualifyingAccountName();
  }

  private async reloadProgram(): Promise<void> {
    const p = await this.programsService.get(this.programId());
    this.program.set(p);
    this.programContext.updateName(p);
    this.infoForm.patchValue({
      name: p.name,
      description: p.description ?? '',
      slug: p.slug,
    });
  }

  private async loadQualifyingAccountName(): Promise<void> {
    const accountTypes = await this.accountTypesService.listAll(this.programId());
    this.qualifyingAccountName.set(
      accountTypes.find((a: AccountType) => a.isTierQualifying)?.name ?? null,
    );
  }

  protected async saveInfo(): Promise<void> {
    if (this.infoForm.invalid || this.savingInfo()) return;
    this.savingInfo.set(true);
    try {
      const v = this.infoForm.getRawValue();
      const current = this.program();
      // Status is not part of Save — the switch applies it on its own (1.3.CL item 7). The slug
      // is only sent when changed on a draft; the API locks it once published (A5).
      const slugChanged = current?.publicationStatus === 'draft' && v.slug !== current.slug;
      const updated = await this.programsService.update(this.programId(), {
        name: v.name,
        description: v.description || null,
        ...(slugChanged ? { slug: v.slug } : {}),
      });
      this.program.set(updated);
      this.programContext.updateName(updated);
      this.toast.success('Program saved');
    } catch (err) {
      if (err instanceof ApiError) this.toast.error(err.message);
      else this.toast.error('Something went wrong. Please try again.');
    } finally {
      this.savingInfo.set(false);
    }
  }

  protected async toggleStatus(): Promise<void> {
    const p = this.program();
    if (!p || p.publicationStatus === 'draft' || this.togglingStatus()) return;
    this.togglingStatus.set(true);
    try {
      const updated = await this.programsService.update(p.id, {
        status: p.status === 'active' ? 'inactive' : 'active',
      });
      this.program.set(updated);
      this.toast.success(
        String(
          this.translate.instant(
            updated.status === 'active'
              ? 'programs.status.activated'
              : 'programs.status.deactivated',
          ),
        ),
      );
    } catch (err) {
      if (err instanceof ApiError) this.toast.error(err.message);
      else this.toast.error('Something went wrong. Please try again.');
    } finally {
      this.togglingStatus.set(false);
    }
  }

  protected async publish(): Promise<void> {
    const p = this.program();
    if (!p || !this.canPublish() || this.publishing()) return;

    const confirmed = await this.confirm.ask({
      title: String(this.translate.instant('programs.publication.confirmTitle')),
      message: String(
        this.translate.instant('programs.publication.confirmMessage', { name: p.name }),
      ),
      confirmLabel: String(this.translate.instant('programs.publication.publish')),
    });
    if (!confirmed) return;

    this.publishing.set(true);
    try {
      const updated = await this.programsService.publish(p.id);
      this.program.set(updated);
      this.toast.success(
        String(
          this.translate.instant('programs.publication.done', {
            version: updated.publishedVersion,
          }),
        ),
      );
    } catch (err) {
      if (err instanceof ApiError) this.toast.error(err.message);
      else this.toast.error('Something went wrong. Please try again.');
    } finally {
      this.publishing.set(false);
    }
  }

  protected async deleteProgram(): Promise<void> {
    if (this.deleting() || this.isRunning()) return;
    const p = this.program();
    if (!p) return;

    const confirmed = await this.confirm.ask({
      title: 'Delete program?',
      message: `"${p.name}" and its configuration will be removed. This cannot be undone.`,
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!confirmed) return;

    this.deleting.set(true);
    try {
      await this.programsService.delete(p.id);
      this.toast.success(`Program "${p.name}" deleted`);
      await this.router.navigate(['/programs']);
    } catch (err) {
      if (err instanceof ApiError) this.toast.error(err.message);
      else this.toast.error('Something went wrong. Please try again.');
    } finally {
      this.deleting.set(false);
    }
  }
}
