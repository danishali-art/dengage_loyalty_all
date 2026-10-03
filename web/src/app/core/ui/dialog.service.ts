import { Injectable, inject } from '@angular/core';
import { ComponentType } from '@angular/cdk/portal';
import { Dialog, DialogConfig, DialogRef } from '@angular/cdk/dialog';
import { Overlay } from '@angular/cdk/overlay';

const EXIT_ANIMATION_MS = 120;

/**
 * Thin wrapper over CDK `Dialog` so features depend on our seam, not the CDK directly.
 * CDK already provides focus trap + restore, `role="dialog"`, `aria-modal` and Esc-to-close.
 */
@Injectable({ providedIn: 'root' })
export class DialogService {
  private readonly cdk = inject(Dialog);
  private readonly overlay = inject(Overlay);

  open<TResult, TData = unknown, TComp = unknown>(
    component: ComponentType<TComp>,
    config?: DialogConfig<TData, DialogRef<TResult, TComp>>,
  ): DialogRef<TResult, TComp> {
    return this.cdk.open<TResult, TData, TComp>(component, {
      hasBackdrop: true,
      disableClose: false,
      panelClass: 'app-dialog-panel',
      backdropClass: 'app-dialog-backdrop',
      ...config,
    });
  }

  /** A drawer docked to the right edge, full height — pair with the `SidePanel` chrome. */
  openSidePanel<TResult, TData = unknown, TComp = unknown>(
    component: ComponentType<TComp>,
    config?: DialogConfig<TData, DialogRef<TResult, TComp>>,
  ): DialogRef<TResult, TComp> {
    return this.open<TResult, TData, TComp>(component, {
      positionStrategy: this.overlay.position().global().right('0').top('0'),
      height: '100vh',
      ...config,
    });
  }
}

/**
 * Close a dialog via its explicit close affordances (✕ / Cancel — the paths every dialog in
 * this app funnels through) with a short fade-out first, instead of the panel just vanishing.
 * Backdrop-click and Escape still close instantly (CDK closes those synchronously, before app
 * code gets a chance to intercept) — an accepted, minor gap rather than deeper CDK interception.
 */
export function closeDialogAnimated<TResult, TComp = unknown>(
  ref: DialogRef<TResult, TComp>,
  result?: TResult,
): void {
  const panel = document.querySelector('.app-dialog-panel');
  if (!panel) {
    ref.close(result);
    return;
  }
  panel.classList.add('app-dialog-closing');
  setTimeout(() => ref.close(result), EXIT_ANIMATION_MS);
}
