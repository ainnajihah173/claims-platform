import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, EMPTY, forkJoin, switchMap, timer } from 'rxjs';
import { problemMessage } from '../core/problem';
import { MARKET_NAMES } from '../shared/format';
import { ExposureRow, Workload } from '../shared/models';

export const POLL_MS = 15_000;

// Staff only (route data.roles). Polls both endpoints and shows each "as of": exposure is event-fed and can lag
// a second or two, workload is a live query (see decisions.md, Dashboards).
@Component({
  selector: 'app-dashboard-page',
  imports: [DatePipe, DecimalPipe, RouterLink],
  template: `
    <a class="crumb" routerLink="/claims">← Claims</a>
    <div class="title-row">
      <div>
        <h1>Dashboard</h1>
        <p class="finding">
          @if (workload(); as w) { Workload as of {{ w.asOf | date: 'HH:mm:ss' }} · refreshes every 15 s }
          @else { Loading… }
        </p>
      </div>
    </div>
    @if (error()) { <p class="notice-error" role="alert">Couldn't refresh. {{ error() }}</p> }

    <section class="panel">
      <div class="panel-head"><h2>Open exposure</h2><span class="muted">per market, currency and type</span></div>
      <table>
        <thead>
          <tr><th scope="col">Market</th><th scope="col">Type</th><th scope="col" class="num">Open</th>
            <th scope="col" class="num">Total reserve</th><th scope="col">As of</th></tr>
        </thead>
        <tbody>
          @for (r of exposure(); track r.market + r.type) {
            <tr>
              <td>{{ markets[r.market] }}</td><td>{{ r.type }}</td><td class="num">{{ r.openCount }}</td>
              <td class="num">{{ r.totalReserve | number: '1.2-2' }} <span class="muted">{{ r.currency }}</span></td>
              <td class="muted num">{{ r.asOf | date: 'd MMM HH:mm:ss' }}</td>
            </tr>
          } @empty {
            <tr><td colspan="5" class="muted">No exposure yet.</td></tr>
          }
        </tbody>
      </table>
    </section>

    @if (workload(); as w) {
      <section class="panel">
        <div class="panel-head"><h2>Workload</h2><span class="muted">{{ w.unassigned }} waiting to be picked up</span></div>
        <table>
          <thead>
            <tr><th scope="col">Officer</th><th scope="col" class="num">Open</th><th scope="col" class="num">Awaiting info</th>
              <th scope="col" class="num">Overdue</th><th scope="col" class="num">Oldest open (days)</th>
              <th scope="col" class="num">Closed, 30 days</th><th scope="col" class="num">Avg days to close</th></tr>
          </thead>
          <tbody>
            @for (o of w.officers; track o.officerId) {
              <tr>
                <td>{{ o.name }}</td><td class="num">{{ o.open }}</td><td class="num">{{ o.awaitingInfo }}</td>
                <td class="num" [class.overdue]="o.overdue > 0">{{ o.overdue }}</td>
                <td class="num">{{ o.oldestOpenDays ?? '–' }}</td><td class="num">{{ o.closedLast30Days }}</td>
                <td class="num">{{ o.avgDaysToCloseLast30Days ?? '–' }}</td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    }
  `,
  styles: `
    .panel { margin-bottom: var(--gap); overflow-x: auto; }
    .notice-error { margin-bottom: var(--gap); }
    table { width: 100%; border-collapse: collapse; margin-top: 8px; }
    th { font-size: var(--fs-small); font-weight: 400; color: var(--muted); text-align: left; height: 40px; padding: 0 20px;
      border-bottom: 1px solid var(--border); }
    td { padding: 10px 20px; border-top: 1px solid var(--rule); white-space: nowrap; }
    tbody tr:first-child td { border-top: 0; }
    .num { text-align: right; }
  `,
})
export class DashboardPage {
  private http = inject(HttpClient);
  protected readonly markets = MARKET_NAMES;
  protected readonly exposure = signal<ExposureRow[]>([]);
  protected readonly workload = signal<Workload | null>(null);
  protected readonly error = signal('');

  constructor() {
    timer(0, POLL_MS).pipe(
      // A failed poll shows the error and keeps the last numbers; the next tick tries again.
      switchMap(() => forkJoin({
        exposure: this.http.get<ExposureRow[]>('/api/dashboard/exposure'),
        workload: this.http.get<Workload>('/api/dashboard/workload'),
      }).pipe(catchError(e => { this.error.set(problemMessage(e)); return EMPTY; }))),
      takeUntilDestroyed(),
    ).subscribe(r => {
      this.error.set('');
      this.exposure.set(r.exposure);
      this.workload.set(r.workload);
    });
  }
}
