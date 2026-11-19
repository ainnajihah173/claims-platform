import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { Auth } from '../core/auth';
import { problemMessage } from '../core/problem';
import { MARKET_NAMES, nameOf, STATUS_LABELS, STATUS_TONE } from '../shared/format';
import { ClaimSummary } from '../shared/models';

interface Filter { key: string; label: string; empty: string; rows: ClaimSummary[] }

const closed = (c: ClaimSummary) => c.status === 'Settled' || c.status === 'Rejected';
const oldestFirst = (a: ClaimSummary, b: ClaimSummary) => a.submittedAt.localeCompare(b.submittedAt);
const newestFirst = (a: ClaimSummary, b: ClaimSummary) => b.submittedAt.localeCompare(a.submittedAt);

// One list for both roles. queue=Mine means "assigned to me" for staff and "submitted by me" for claimants;
// staff also get the unassigned queue. The count cards filter the one table. Age and overdue are staff-only
// (the target is an internal SLA).
@Component({
  selector: 'app-claim-list-page',
  imports: [RouterLink, DatePipe, DecimalPipe],
  template: `
    <div class="title-row">
      <div>
        <h1>{{ staff ? 'Claims' : 'My claims' }}</h1>
        <p class="finding">
          @if (!loaded()) { Loading claims… } @else { {{ finding() }} }
        </p>
      </div>
      @if (staff) { <a class="btn btn-secondary" routerLink="/dashboard">Dashboard</a> }
      @else { <a class="btn btn-primary" routerLink="/claims/new">Submit a claim</a> }
    </div>

    <div class="filters" role="group" aria-label="Filter claims" [style.--n]="filters().length">
      @for (f of filters(); track f.key) {
        <button class="count" [class.selected]="f.key === current()?.key"
                [attr.aria-pressed]="f.key === current()?.key" (click)="selected.set(f.key)">
          <span class="count-label">{{ f.label }}</span>
          <span class="count-value num">
            @if (loaded()) { {{ f.rows.length }} } @else { <span class="skeleton" style="width: 24px; height: 20px"></span> }
          </span>
        </button>
      }
    </div>

    <section class="panel list" [attr.aria-label]="current()?.label">
      @if (error()) {
        <div class="panel-body">
          <p class="notice-error" role="alert">
            <span>Couldn't load claims. {{ error() }}</span>
            <button class="btn btn-ghost" (click)="load()">Retry</button>
          </p>
        </div>
      } @else if (loaded() && current()?.rows?.length === 0) {
        <div class="panel-body"><p class="empty">{{ current()?.empty }}</p></div>
      } @else {
        <table>
          <thead>
            <tr>
              <th scope="col">Claim</th>
              <th scope="col">Submitted</th>
              <th scope="col">Status</th>
              <th scope="col" class="num">Reserve</th>
            </tr>
          </thead>
          <tbody>
            @if (!loaded()) {
              @for (i of [1, 2, 3]; track i) {
                <tr class="loading">
                  <td class="c-claim"><span class="skeleton" style="width: 160px"></span><span class="skeleton sub" style="width: 96px"></span></td>
                  <td class="c-date"><span class="skeleton" style="width: 88px"></span></td>
                  <td class="c-status"><span class="skeleton" style="width: 72px; height: 22px"></span></td>
                  <td class="c-amount"><span class="skeleton" style="width: 96px; margin-left: auto"></span></td>
                </tr>
              }
            }
            @for (c of current()?.rows ?? []; track c.id) {
              <tr>
                <td class="c-claim">
                  <a class="claim-link" [routerLink]="['/claims', c.id]">{{ c.type }} · {{ markets[c.market] }}</a>
                  <span class="sub">
                    @if (staff) { {{ c.claimantId === me ? 'You' : name(c.claimantId) }} }
                    @else { {{ c.assignedOfficerId ? 'With ' + name(c.assignedOfficerId) : 'Not picked up yet' }} }
                  </span>
                </td>
                <td class="c-date">
                  <span class="num">{{ c.submittedAt | date: 'd MMM y' }}</span>
                  @if (staff && !isClosed(c)) {
                    <span class="sub num" [class.overdue]="c.isOverdue">{{ c.ageDays }} {{ c.ageDays === 1 ? 'day' : 'days' }}{{ c.isOverdue ? ' · overdue' : '' }}</span>
                  }
                </td>
                <td class="c-status"><span class="pill pill-{{ tone[c.status] }}">{{ labels[c.status] }}</span></td>
                <td class="c-amount num">
                  <span class="money">{{ c.reserve | number: '1.2-2' }} <span class="unit">{{ c.currency }}</span></span>
                  @if (c.settledAmount !== null) {
                    <span class="sub">Settled {{ c.settledAmount | number: '1.2-2' }}</span>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
  styles: `
    .filters { display: grid; grid-template-columns: repeat(var(--n), 1fr); gap: var(--gap); margin-bottom: var(--gap); }
    .count {
      display: flex; flex-direction: column; align-items: flex-start; gap: 2px; padding: 12px 14px; min-height: 64px;
      background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-card);
      cursor: pointer; text-align: left; transition: border-color .12s;
    }
    .count:hover { border-color: var(--control); }
    .count.selected { border: 1.5px solid var(--text); padding: 11.5px 13.5px; background: #fbfbfc; }
    .count-label { font-size: 13px; color: var(--muted); }
    .count-value { font-size: var(--fs-section); line-height: 24px; font-weight: 500; min-height: 24px; display: flex; align-items: center; }

    table { width: 100%; border-collapse: collapse; }
    th {
      font-size: var(--fs-small); font-weight: 400; color: var(--muted); text-align: left;
      height: 40px; padding: 0 20px; border-bottom: 1px solid var(--border);
    }
    th.num, td.c-amount { text-align: right; }
    td { padding: 12px 20px; vertical-align: top; border-top: 1px solid var(--rule); }
    tbody tr:first-child td { border-top: 0; }
    tbody tr { position: relative; }
    tbody tr:not(.loading):hover { background: #fafbfc; }
    td.c-claim { width: 40%; }
    .claim-link { font-weight: 500; text-decoration: none; }
    .claim-link::after { content: ''; position: absolute; inset: 0; }
    .claim-link:focus-visible { outline: none; }
    tr:has(.claim-link:focus-visible) { outline: 2px solid var(--hue); outline-offset: -2px; }
    .sub { display: block; margin-top: 2px; font-size: 13px; color: var(--muted); }
    .sub.overdue { color: var(--negative); }
    .money { font-weight: 500; }
    .unit { color: var(--muted); font-weight: 400; font-size: var(--fs-small); }
    .skeleton.sub { margin-top: 8px; }

    @media (max-width: 640px) {
      .filters { display: flex; flex-wrap: wrap; gap: 8px; }
      .count { flex: none; flex-direction: row; align-items: center; gap: 8px; min-height: 44px; padding: 0 14px; border-radius: 999px; }
      .count.selected { padding: 0 13.5px; }
      .count-label { font-size: var(--fs-body); color: var(--text); }
      thead { display: none; }
      tbody tr {
        display: grid; grid-template-columns: 1fr auto; grid-template-areas: "claim amount" "date status";
        gap: 6px 12px; padding: 14px 16px; border-top: 1px solid var(--rule);
      }
      tbody tr:first-child { border-top: 0; }
      td { padding: 0; border: 0; width: auto !important; }
      td.c-claim { grid-area: claim; }
      td.c-amount { grid-area: amount; }
      td.c-date { grid-area: date; display: flex; gap: 6px; flex-wrap: wrap; color: var(--muted); font-size: 13px; }
      td.c-date .sub { margin: 0; }
      td.c-date .sub::before { content: '· '; }
      td.c-status { grid-area: status; justify-self: end; }
    }
  `,
})
export class ClaimListPage {
  private http = inject(HttpClient);
  private auth = inject(Auth);
  protected readonly staff = this.auth.isStaff();
  protected readonly me = this.auth.user()?.id;
  protected readonly markets = MARKET_NAMES;
  protected readonly labels = STATUS_LABELS;
  protected readonly tone = STATUS_TONE;
  protected readonly name = nameOf;
  protected readonly isClosed = closed;

  private readonly mine = signal<ClaimSummary[]>([]);
  private readonly queue = signal<ClaimSummary[]>([]);
  protected readonly loaded = signal(false);
  protected readonly error = signal('');
  protected readonly selected = signal<string | null>(null);

  // Counts follow the rows: each card's number is the length of the rows it shows.
  protected readonly filters = computed<Filter[]>(() => {
    const mine = this.mine();
    if (this.staff) {
      return [
        { key: 'pickup', label: 'To pick up', empty: 'Nothing waiting to be picked up.', rows: [...this.queue()].sort(oldestFirst) },
        { key: 'review', label: 'In review', empty: 'None of your claims are in review.', rows: mine.filter(c => c.status === 'InReview').sort(oldestFirst) },
        { key: 'waiting', label: 'Awaiting info', empty: 'No claims are waiting on a claimant.', rows: mine.filter(c => c.status === 'AwaitingInfo').sort(oldestFirst) },
        { key: 'closed', label: 'Closed', empty: 'You haven’t closed a claim yet.', rows: mine.filter(closed).sort(newestFirst) },
      ];
    }
    return [
      { key: 'answer', label: 'Needs your answer', empty: 'Nothing needs your answer.', rows: mine.filter(c => c.status === 'AwaitingInfo').sort(oldestFirst) },
      { key: 'open', label: 'In progress', empty: 'No claims in progress.', rows: mine.filter(c => c.status === 'Submitted' || c.status === 'InReview').sort(oldestFirst) },
      { key: 'closed', label: 'Closed', empty: 'No closed claims yet.', rows: mine.filter(closed).sort(newestFirst) },
    ];
  });

  // Until the user picks one, show the first card that has claims in it.
  protected readonly current = computed(() => {
    const fs = this.filters();
    return fs.find(f => f.key === this.selected()) ?? fs.find(f => f.rows.length > 0) ?? fs[0];
  });

  protected readonly finding = computed(() => {
    const mine = this.mine();
    if (this.staff) {
      const yours = mine.filter(c => !closed(c) && c.isOverdue).length;
      const queued = this.queue().filter(c => c.isOverdue).length;
      if (yours + queued === 0) return 'Nothing is overdue.';
      const parts = [yours && `${yours} of yours`, queued && `${queued} waiting to be picked up`].filter(Boolean);
      return `Overdue: ${parts.join(', ')}.`;
    }
    const answer = mine.filter(c => c.status === 'AwaitingInfo').length;
    if (answer > 0) return `${answer} ${answer === 1 ? 'claim needs' : 'claims need'} your answer.`;
    const open = mine.filter(c => !closed(c)).length;
    if (mine.length === 0) return 'You have no claims yet.';
    return open > 0 ? `${open} in progress, nothing needed from you.` : 'All your claims are closed.';
  });

  // shortcut: loaded once per visit, no polling; re-open the page to refresh (see decisions.md, Frontend).
  constructor() {
    this.load();
  }

  protected load() {
    this.error.set('');
    this.loaded.set(false);
    forkJoin({
      mine: this.http.get<ClaimSummary[]>('/api/claims?queue=Mine'),
      queue: this.staff ? this.http.get<ClaimSummary[]>('/api/claims?queue=Unassigned') : of([]),
    }).subscribe({
      next: r => { this.mine.set(r.mine); this.queue.set(r.queue); this.loaded.set(true); },
      error: e => this.error.set(problemMessage(e)),
    });
  }
}
