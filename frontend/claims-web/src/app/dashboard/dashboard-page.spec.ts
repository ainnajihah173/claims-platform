import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DashboardPage, POLL_MS } from './dashboard-page';

describe('DashboardPage', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });
  afterEach(() => vi.useRealTimers());

  const answerBoth = () => {
    backend.expectOne('/api/dashboard/exposure').flush([]);
    backend.expectOne('/api/dashboard/workload').flush({ asOf: '2026-11-20T00:00:00Z', unassigned: 0, officers: [] });
  };

  it('loads at once, polls every 15 s, keeps polling after an error and stops when closed', () => {
    const page = TestBed.createComponent(DashboardPage);
    vi.advanceTimersByTime(0);
    answerBoth();

    vi.advanceTimersByTime(POLL_MS);
    backend.expectOne('/api/dashboard/exposure').flush(null, { status: 500, statusText: 'Server Error' });
    backend.expectOne('/api/dashboard/workload'); // cancelled by the failed partner

    vi.advanceTimersByTime(POLL_MS);
    answerBoth();

    page.destroy();
    vi.advanceTimersByTime(POLL_MS);
    backend.verify();
  });
});
