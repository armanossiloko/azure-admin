import { HttpErrorResponse } from '@angular/common/http';

/**
 * Extracts the user-facing message from a failed API call.
 *
 * The backend always answers errors with JSON carrying a top-level `message` (hand-written
 * `{ message }` bodies and RFC 7807 problem details alike), so that is the first choice. Plain-text
 * bodies and the transport error message are fallbacks; `fallback` is used when nothing usable exists.
 */
export function apiErrorMessage(error: unknown, fallback = 'Request failed.'): string {
  if (error instanceof HttpErrorResponse || isHttpLike(error)) {
    const body = (error as { error?: unknown }).error;
    if (typeof body === 'object' && body !== null) {
      const record = body as Record<string, unknown>;
      for (const key of ['message', 'errorMessage', 'detail', 'title']) {
        const value = record[key];
        if (typeof value === 'string' && value.trim()) return value.trim();
      }
    }
    if (typeof body === 'string' && body.trim()) return body.trim();
    const message = (error as { message?: unknown }).message;
    if (typeof message === 'string' && message.trim() && !isTransportNoise(message)) return message;
    return fallback;
  }
  if (error instanceof Error && error.message) return error.message;
  return fallback;
}

/** True when the failure was a specific HTTP status (e.g. 404), not a network or parsing error. */
export function apiErrorStatus(error: unknown): number | null {
  const status = (error as { status?: unknown })?.status;
  return typeof status === 'number' && status > 0 ? status : null;
}

function isHttpLike(error: unknown): boolean {
  return typeof error === 'object' && error !== null && 'error' in error;
}

/** Angular's generic "Http failure response for /api/x: 500 OK" is not worth showing to a user. */
function isTransportNoise(message: string): boolean {
  return message.startsWith('Http failure response');
}
