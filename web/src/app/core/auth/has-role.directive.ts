import { Directive, Input, TemplateRef, ViewContainerRef, effect, inject } from '@angular/core';
import { SessionStore } from './session.store';
import { UserRole } from './jwt';

/**
 * `*appHasRole="'platform_admin'"` or `*appHasRole="['platform_admin','tenant_admin']"`.
 * Cosmetic only — route access is enforced by `roleGuard`.
 */
@Directive({ selector: '[appHasRole]' })
export class HasRoleDirective {
  private readonly tpl = inject(TemplateRef<unknown>);
  private readonly vcr = inject(ViewContainerRef);
  private readonly session = inject(SessionStore);

  private allowed: readonly UserRole[] = [];
  private rendered = false;

  @Input({ required: true })
  set appHasRole(roles: UserRole | readonly UserRole[]) {
    this.allowed = Array.isArray(roles) ? roles : [roles as UserRole];
  }

  constructor() {
    effect(() => {
      const role = this.session.role();
      const show = role !== null && this.allowed.includes(role);
      if (show && !this.rendered) {
        this.vcr.createEmbeddedView(this.tpl);
        this.rendered = true;
      } else if (!show && this.rendered) {
        this.vcr.clear();
        this.rendered = false;
      }
    });
  }
}
