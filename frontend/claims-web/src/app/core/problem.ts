import { HttpErrorResponse } from '@angular/common/http';

// ProblemDetails from the API: domain/concurrency errors carry `detail`, validation errors carry `errors`.
export function problemMessage(e: HttpErrorResponse): string {
  const p = e.error;
  if (p?.detail) return p.detail;
  if (p?.errors) return Object.values(p.errors as Record<string, string[]>).flat().join(' ');
  return p?.title ?? e.message;
}
