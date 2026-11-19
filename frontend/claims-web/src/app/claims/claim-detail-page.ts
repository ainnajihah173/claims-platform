import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Auth } from '../core/auth';
import { problemMessage } from '../core/problem';
import { EVENT_LABELS, initialOf, MARKET_NAMES, nameOf, personHue, STATUS_LABELS, STATUS_TONE } from '../shared/format';
import { ClaimDetail, TimelineItem } from '../shared/models';

interface Step { label: string; at: string | null; who: string; state: 'done' | 'current' | 'todo'; note?: string }

// One detail page for both roles. Buttons only appear when the action can work (status + assigned officer),
// but the API still enforces every rule: a 409 from it is shown as-is.
@Component({
  selector: 'app-claim-detail-page',
  imports: [RouterLink, DatePipe, DecimalPipe],
  template: `
    <a class="crumb" routerLink="/claims"><span aria-hidden="true">←</span> {{ staff ? 'Claims' : 'My claims' }}</a>

    @if (detail(); as d) {
      <div class="title-row">
        <div>
          <div class="h1-line">
            <h1>{{ d.claim.type }} claim · {{ markets[d.claim.market] }}</h1>
            <span class="pill pill-{{ tone[d.claim.status] }}">{{ labels[d.claim.status] }}</span>
          </div>
          <p class="description">{{ d.description }}</p>
          <p class="finding">
            @if (staff && d.claim.isOverdue) { <strong class="overdue">Overdue, {{ d.claim.ageDays }} days open.</strong> }
            {{ finding() }}
          </p>
        </div>
      </div>

      <!-- The claim's route: where it has been and where it is now. -->
      <ol class="route panel" aria-label="Progress">
        @for (s of route(); track s.label) {
          <li class="step {{ s.state }}" [attr.aria-current]="s.state === 'current' ? 'step' : null">
            <span class="step-bar" aria-hidden="true"></span>
            <span class="step-label">{{ s.label }}</span>
            <span class="step-meta">
              @if (s.at) { {{ s.at | date: 'd MMM y, HH:mm' }} · {{ s.who }} } @else { {{ s.who }} }
            </span>
            @if (s.note) { <span class="step-note">{{ s.note }}</span> }
          </li>
        }
      </ol>

      <section class="panel figures" aria-label="Amounts">
        <div class="figure">
          <span class="figure-label">Reserve</span>
          <span class="figure-value num" [class.money]="d.claim.settledAmount === null">{{ d.claim.reserve | number: '1.2-2' }} <span class="unit">{{ d.claim.currency }}</span></span>
        </div>
        <div class="figure">
          <span class="figure-label">Settled</span>
          @if (d.claim.settledAmount !== null) {
            <span class="figure-value num money">{{ d.claim.settledAmount | number: '1.2-2' }} <span class="unit">{{ d.claim.currency }}</span></span>
          } @else { <span class="figure-value muted" aria-label="Not settled">–</span> }
        </div>
        <div class="figure">
          @if (staff && !d.claim.closedAt) {
            <span class="figure-label">Open for</span>
            <span class="figure-value num" [class.overdue]="d.claim.isOverdue">{{ d.claim.ageDays }} <span class="unit">{{ d.claim.ageDays === 1 ? 'day' : 'days' }}</span></span>
          } @else if (d.claim.closedAt) {
            <span class="figure-label">Closed</span>
            <span class="figure-value num">{{ d.claim.closedAt | date: 'd MMM y' }}</span>
          } @else {
            <span class="figure-label">Submitted</span>
            <span class="figure-value num">{{ d.claim.submittedAt | date: 'd MMM y' }}</span>
          }
        </div>
        <div class="figure">
          <span class="figure-label">Policy</span>
          <span class="figure-value policy mono">{{ d.policyNumber }}</span>
        </div>
      </section>

      <div class="grid">
        <section class="panel next" [class.short]="!mineToHandle() && !(!staff && d.claim.status === 'AwaitingInfo')" aria-labelledby="next-head">
          <div class="panel-head">
            <h2 id="next-head">
              @if (!staff && d.claim.status === 'AwaitingInfo') { {{ name(d.claim.assignedOfficerId) }} needs more information }
              @else { Next step }
            </h2>
          </div>

          @if (mineToHandle()) {
            <div class="panel-body actions">
              <div class="action-group">
                <div class="field">
                  <label for="text">{{ d.claim.status === 'InReview' ? 'Question for the claimant, or reason for rejecting' : 'Reason for rejecting' }}</label>
                  <textarea id="text" #text rows="3"></textarea>
                </div>
                <div class="buttons">
                  @if (d.claim.status === 'InReview') {
                    <button class="btn btn-secondary" (click)="act('POST', 'request-info', { question: text.value })">Request info</button>
                  }
                  <button class="btn btn-danger" (click)="act('POST', 'reject', { reason: text.value })">Reject claim</button>
                </div>
              </div>
              <div class="action-group">
                <div class="field">
                  <label for="amount">Amount</label>
                  <div class="input-affix">
                    <!-- valueAsNumber is NaN when empty → sent as null → API 400, never a silent 0 reserve -->
                    <input id="amount" #amount type="number" min="0" step="0.01" inputmode="decimal" aria-describedby="amount-hint" />
                    <span>{{ d.claim.currency }}</span>
                  </div>
                  <p id="amount-hint" class="hint">
                    Current reserve {{ d.claim.reserve | number: '1.2-2' }} {{ d.claim.currency }}.
                    @if (d.claim.status === 'InReview') { A settlement can’t be more than the reserve. }
                  </p>
                </div>
                <div class="buttons">
                  <button class="btn btn-secondary" (click)="act('PUT', 'reserve', { amount: amount.valueAsNumber })">Set reserve</button>
                  @if (d.claim.status === 'InReview') {
                    <button class="btn btn-primary" (click)="act('POST', 'settle', { amount: amount.valueAsNumber })">Settle</button>
                  }
                </div>
              </div>
              @if (error()) { <p class="notice-error span" role="alert">{{ error() }}</p> }
            </div>
          } @else if (!staff && d.claim.status === 'AwaitingInfo') {
            <div class="panel-body stack">
              <blockquote>{{ openQuestion()?.note }}</blockquote>
              <div class="field">
                <label for="answer">Your answer</label>
                <textarea id="answer" #answer rows="4"></textarea>
              </div>
              <div class="buttons">
                <button class="btn btn-primary" (click)="act('POST', 'provide-info', { answer: answer.value })">Send answer</button>
              </div>
              @if (error()) { <p class="notice-error" role="alert">{{ error() }}</p> }
            </div>
          } @else {
            <div class="panel-body stack">
              <p class="next-note">{{ nextNote() }}</p>
              @if (staff && d.claim.status === 'Submitted') {
                <div class="buttons">
                  <button class="btn btn-primary" (click)="act('POST', 'assign')">Pick up this claim</button>
                </div>
              }
              @if (error()) { <p class="notice-error" role="alert">{{ error() }}</p> }
            </div>
          }
        </section>

        <section class="panel timeline" aria-labelledby="timeline-head">
          <div class="panel-head">
            <h2 id="timeline-head">Timeline</h2>
            <span class="muted num">{{ d.timeline.length }} {{ d.timeline.length === 1 ? 'entry' : 'entries' }}</span>
          </div>
          <ol class="panel-body">
            @for (t of d.timeline; track $index; let last = $last) {
              <li [class.latest]="last">
                <span class="avatar" [style.--h]="hue(t.actorId)" aria-hidden="true">{{ initial(t.actorId) }}</span>
                <div class="entry">
                  <p class="entry-title">
                    {{ events[t.eventType] }}
                    @if (t.eventType === 'ReserveSet') {
                      <span class="num">{{ toNumber(t.oldValue) | number: '1.2-2' }} → {{ toNumber(t.newValue) | number: '1.2-2' }} {{ d.claim.currency }}</span>
                    }
                    @if (t.eventType === 'Settled' && d.claim.settledAmount !== null) {
                      <span class="num">for {{ d.claim.settledAmount | number: '1.2-2' }} {{ d.claim.currency }}</span>
                    }
                  </p>
                  <p class="entry-meta"><span>{{ name(t.actorId) }}</span> · <time class="num" [attr.datetime]="t.at">{{ t.at | date: 'd MMM y, HH:mm' }}</time></p>
                  @if (t.note && t.eventType !== 'Settled') { <blockquote>{{ t.note }}</blockquote> }
                </div>
              </li>
            }
          </ol>
        </section>
      </div>
    } @else if (error()) {
      <p class="notice-error" role="alert">
        <span>Couldn't load this claim. {{ error() }}</span>
        <button class="btn btn-ghost" (click)="load()">Retry</button>
      </p>
    } @else {
      <div class="title-row"><div><span class="skeleton" style="width: 320px; height: 32px"></span>
        <span class="skeleton" style="width: 240px; margin-top: 10px"></span></div></div>
      <div class="panel route-skeleton"></div>
    }
  `,
  styles: `
    .h1-line { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .description { margin-top: 6px; font-size: var(--fs-section); line-height: 24px; }

    .route { list-style: none; margin: 0 0 var(--gap); padding: 16px 20px; display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; }
    .step { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .step-bar { height: 4px; border-radius: 2px; background: var(--raised); margin-bottom: 10px; }
    .step.done .step-bar { background: var(--text); }
    .step.current .step-bar { background: var(--hue); }
    .step-label { font-weight: 500; }
    .step.todo .step-label { color: var(--muted); }
    .step-meta { font-size: 13px; color: var(--muted); }
    .step-note { font-size: 13px; color: var(--pill-warning-ink); font-weight: 500; }
    .route-skeleton { height: 92px; }

    /* Next step and the timeline share a row and its bottom edge; the shorter card's buttons sit at its foot. */
    .grid { display: grid; grid-template-columns: minmax(0, 1fr) 400px; gap: var(--gap); }
    .next { display: flex; flex-direction: column; }
    .next .panel-body { flex: 1; }
    .next.short { align-self: start; } /* one sentence: keep its own height, never stretch over blank */

    .figures { display: grid; grid-template-columns: 1.3fr 1fr 1fr 1fr; margin-bottom: var(--gap); }
    .figure { display: flex; flex-direction: column; gap: 4px; padding: 16px 20px; min-width: 0; }
    .figure + .figure { border-left: 1px solid var(--border); }
    .figure-label { font-size: 13px; color: var(--muted); }
    .figure-value { font-size: 22px; line-height: 30px; white-space: nowrap; }
    .figure-value.money { font-size: var(--fs-kpi); line-height: 36px; font-weight: 700; letter-spacing: -0.01em; }
    .figure-value.policy { font-size: 16px; }
    .unit { font-size: 14px; color: var(--muted); font-weight: 400; letter-spacing: 0; }
    .figure-value.overdue .unit { color: var(--negative); }

    .actions { display: grid; grid-template-columns: 1fr 1fr; grid-template-rows: 1fr auto; gap: 24px; }
    .action-group { display: flex; flex-direction: column; justify-content: space-between; gap: 12px; }
    .buttons { display: flex; gap: 8px; flex-wrap: wrap; }
    .hint { font-size: 13px; color: var(--muted); }
    .span { grid-column: 1 / -1; }
    .stack { display: flex; flex-direction: column; gap: 16px; }
    .stack .buttons { margin-top: auto; }
    .next-note { font-size: 15px; line-height: 22px; max-width: 60ch; }
    blockquote { margin: 0; padding: 10px 14px; background: var(--ground); border-radius: var(--radius-control); line-height: 20px; }

    .timeline ol { list-style: none; margin: 0; }
    .timeline li { display: grid; grid-template-columns: 28px 1fr; gap: 12px; position: relative; padding-bottom: 18px; }
    .timeline li:last-child { padding-bottom: 0; }
    .timeline li:not(:last-child)::before {
      content: ''; position: absolute; left: 13.5px; top: 32px; bottom: 4px; width: 1px; background: var(--border);
    }
    .timeline li.latest .avatar { outline: 2px solid var(--hue); outline-offset: 2px; }
    .entry { min-width: 0; padding-top: 4px; display: flex; flex-direction: column; gap: 2px; }
    .entry-title { font-weight: 500; }
    .entry-title .num { font-weight: 400; color: var(--muted); margin-left: 4px; }
    .entry-meta { font-size: 13px; color: var(--muted); }
    .entry blockquote { margin-top: 6px; }

    @media (max-width: 1100px) {
      .grid { grid-template-columns: 1fr; }
      .figures { grid-template-columns: 1fr 1fr; }
      .figure:nth-child(3) { border-left: 0; }
      .figure:nth-child(n + 3) { border-top: 1px solid var(--border); }
    }
    @media (max-width: 640px) {
      .route { grid-template-columns: 1fr; gap: 14px; }
      .step { display: grid; grid-template-columns: 4px 1fr; column-gap: 12px; }
      .step-bar { grid-row: 1 / span 3; height: auto; margin: 0; }
      .actions { grid-template-columns: 1fr; grid-template-rows: none; }
      .figure-value.money { font-size: 26px; }
    }
  `,
})
export class ClaimDetailPage {
  private http = inject(HttpClient);
  private auth = inject(Auth);
  private id = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly staff = this.auth.isStaff();
  protected readonly detail = signal<ClaimDetail | null>(null);
  protected readonly error = signal('');
  protected readonly markets = MARKET_NAMES;
  protected readonly labels = STATUS_LABELS;
  protected readonly tone = STATUS_TONE;
  protected readonly events = EVENT_LABELS;
  protected readonly name = nameOf;
  protected readonly hue = personHue;
  protected readonly initial = initialOf;
  protected readonly toNumber = (v: string | null) => Number(v ?? 0);

  protected readonly mineToHandle = computed(() => {
    const c = this.detail()?.claim;
    return !!c && this.staff && c.assignedOfficerId === this.auth.user()?.id
      && (c.status === 'InReview' || c.status === 'AwaitingInfo');
  });

  private last(type: TimelineItem['eventType']) {
    return this.detail()?.timeline.filter(t => t.eventType === type).at(-1);
  }
  protected readonly openQuestion = computed(() => this.last('InfoRequested'));

  // One sentence on where the claim stands, built from the same timeline the page shows.
  protected readonly finding = computed(() => {
    const d = this.detail();
    if (!d) return '';
    const c = d.claim;
    const day = (iso?: string | null) => iso ? new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' }) : '';
    const money = (n: number) => n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    switch (c.status) {
      case 'Submitted': return `Submitted ${day(c.submittedAt)}, waiting for an officer to pick it up.`;
      case 'InReview': return `With ${c.assignedOfficerId === this.auth.user()?.id ? 'you' : nameOf(c.assignedOfficerId)} since ${day(this.last('Assigned')?.at)}.`;
      case 'AwaitingInfo': return this.staff
        ? `Waiting on ${nameOf(c.claimantId)}’s answer since ${day(this.openQuestion()?.at)}.`
        : `Your officer asked a question on ${day(this.openQuestion()?.at)}. Answer it below.`;
      case 'Settled': return `Settled for ${money(c.settledAmount ?? 0)} ${c.currency} on ${day(c.closedAt)}.`;
      case 'Rejected': return `Rejected on ${day(c.closedAt)}.`;
    }
  });

  // What happens next when this user has nothing to do on the claim.
  protected readonly nextNote = computed(() => {
    const c = this.detail()?.claim;
    if (!c) return '';
    const officer = nameOf(c.assignedOfficerId);
    switch (c.status) {
      case 'Submitted': return this.staff
        ? 'Waiting for an officer. Picking it up assigns it to you and starts the review.'
        : 'Waiting for an officer to pick it up. You’ll see who has it here.';
      case 'InReview':
      case 'AwaitingInfo': return this.staff
        ? `${officer} has this claim. Only the assigned officer can act on it.`
        : `${officer} is reviewing your claim. If they need anything from you, their question appears here.`;
      case 'Settled': return 'This claim is closed and settled. Nothing more to do.';
      case 'Rejected': return 'This claim is closed. The reason is in the timeline.';
    }
  });

  protected readonly route = computed<Step[]>(() => {
    const d = this.detail();
    if (!d) return [];
    const c = d.claim;
    const assigned = this.last('Assigned');
    const closed = this.last('Settled') ?? this.last('Rejected');
    return [
      { label: 'Submitted', at: c.submittedAt, who: nameOf(c.claimantId), state: c.status === 'Submitted' ? 'current' : 'done' },
      {
        label: 'In review', at: assigned?.at ?? null, who: assigned ? nameOf(assigned.actorId) : 'Not picked up yet',
        state: closed ? 'done' : assigned ? 'current' : 'todo',
        note: c.status === 'AwaitingInfo' ? 'Awaiting the claimant’s answer' : undefined,
      },
      {
        label: closed ? STATUS_LABELS[c.status] : 'Settled or rejected', at: closed?.at ?? null,
        who: closed ? nameOf(closed.actorId) : 'Decided by the officer', state: closed ? 'current' : 'todo',
      },
    ];
  });

  constructor() {
    this.load();
  }

  protected act(method: 'POST' | 'PUT', action: string, body: object = {}) {
    this.error.set('');
    this.http.request(method, `/api/claims/${this.id}/${action}`, { body }).subscribe({
      next: () => this.load(),
      error: e => this.error.set(problemMessage(e)),
    });
  }

  // shortcut: reloads after each action, no polling or push; see decisions.md (Frontend).
  protected load() {
    this.error.set('');
    this.http.get<ClaimDetail>(`/api/claims/${this.id}`).subscribe({
      next: d => this.detail.set(d),
      error: e => this.error.set(problemMessage(e)),
    });
  }
}
