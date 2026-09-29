import {
  Directive,
  Injectable,
  Input,
  TemplateRef,
  ViewContainerRef,
  inject,
} from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { APP_CONFIG, FeatureFlag } from './app-config';

@Injectable({ providedIn: 'root' })
export class FeatureFlagService {
  private readonly config = inject(APP_CONFIG);

  enabled(flag: FeatureFlag): boolean {
    return this.config.features[flag] === true;
  }
}

/** Route guard: blocks a route unless its feature flag is on. */
export function featureGuard(flag: FeatureFlag): CanActivateFn {
  return () => {
    const enabled = inject(FeatureFlagService).enabled(flag);
    return enabled ? true : inject(Router).createUrlTree(['/not-found']);
  };
}

/**
 * Structural directive: `*appFeature="'reports'"` renders its content only when the flag is on.
 * Cosmetic — pair with `featureGuard` for actual route protection.
 */
@Directive({ selector: '[appFeature]' })
export class HasFeatureDirective {
  private readonly tpl = inject(TemplateRef<unknown>);
  private readonly vcr = inject(ViewContainerRef);
  private readonly flags = inject(FeatureFlagService);
  private shown = false;

  @Input({ required: true })
  set appFeature(flag: FeatureFlag) {
    const show = this.flags.enabled(flag);
    if (show && !this.shown) {
      this.vcr.createEmbeddedView(this.tpl);
      this.shown = true;
    } else if (!show && this.shown) {
      this.vcr.clear();
      this.shown = false;
    }
  }
}
