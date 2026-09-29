import { ErrorHandler, Injectable, inject } from '@angular/core';
import { ToastService } from '../ui/toast.service';
import { ApiError } from '../http/api-error';

/** Last line of defence for anything not caught in a stream. */
@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  private readonly toast = inject(ToastService);

  handleError(error: unknown): void {
    // ApiErrors are already surfaced by the error-normalisation interceptor.
    if (error instanceof ApiError) {
      console.error('[ApiError]', error);
      return;
    }

    const message = error instanceof Error ? error.message : 'Something went wrong.';
    console.error('[Unhandled]', error);
    this.toast.error('Unexpected error', { detail: message });
  }
}
