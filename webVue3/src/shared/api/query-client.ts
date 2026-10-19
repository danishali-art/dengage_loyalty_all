import { QueryClient } from '@tanstack/vue-query';
import { ApiError } from './api-error';

/** Port of the Angular retry interceptor's rule: only transient failures, once. Never 4xx. */
const TRANSIENT = new Set([0, 502, 503, 504]);

export function shouldRetry(failureCount: number, error: unknown): boolean {
  return failureCount < 1 && error instanceof ApiError && TRANSIENT.has(error.status);
}

export function createAppQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: shouldRetry,
        retryDelay: (attempt) => 300 * (attempt + 1) + Math.random() * 200,
      },
      // Mutations are never retried: a create/publish must not run twice.
      mutations: { retry: false },
    },
  });
}
