import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Auth, SEED_USERS } from '../core/auth';
import { problemMessage } from '../core/problem';
import { ClaimDetail } from '../shared/models';

// One detail page for both roles. Buttons only appear when the action can work (status + assigned officer),
// but the API still enforces every rule: a 409 from it is shown as-is.
@Component({
  selector: 'app-claim-detail-page',
  imports: [RouterLink, DatePipe, DecimalPipe],
  template: `
    <p><a routerLink="/claims">← Claims</a></p>
    @if (error()) { <p class="error">{{ error() }}</p> }

    @if (detail(); as d) {
      <h1>{{ d.claim.type }} claim · {{ d.claim.market }} · {{ d.claim.status }}</h1>
      <dl>
        <dt>Policy</dt><dd>{{ d.policyNumber }}</dd>
        <dt>Description</dt><dd>{{ d.description }}</dd>
        <dt>Reserve</dt><dd>{{ d.claim.reserve | number: '1.2-2' }} {{ d.claim.currency }}</dd>
        @if (d.claim.settledAmount !== null) {
          <dt>Settled</dt><dd>{{ d.claim.settledAmount | number: '1.2-2' }} {{ d.claim.currency }}</dd>
        }
        <dt>Officer</dt><dd>{{ d.claim.assignedOfficerId ? nameOf(d.claim.assignedOfficerId) : 'Unassigned' }}</dd>
        @if (staff) {
          <dt>Age</dt><dd>{{ d.claim.ageDays }} days @if (d.claim.isOverdue) { 🔴 overdue }</dd>
        }
      </dl>

      @if (staff && d.claim.status === 'Submitted') {
        <button (click)="act('POST', 'assign')">Pick up</button>
      }
      @if (mineToHandle()) {
        <fieldset>
          <legend>Officer actions</legend>
          <p>
            <label>Question / reject reason <textarea #text rows="2"></textarea></label>
            @if (d.claim.status === 'InReview') {
              <button (click)="act('POST', 'request-info', { question: text.value })">Request info</button>
            }
            <button (click)="act('POST', 'reject', { reason: text.value })">Reject</button>
          </p>
          <p>
            <!-- valueAsNumber is NaN when empty → sent as null → API 400, never a silent 0 reserve -->
            <label>Amount ({{ d.claim.currency }}) <input #amount type="number" min="0" step="0.01" /></label>
            <button (click)="act('PUT', 'reserve', { amount: amount.valueAsNumber })">Set reserve</button>
            @if (d.claim.status === 'InReview') {
              <button (click)="act('POST', 'settle', { amount: amount.valueAsNumber })">Settle</button>
            }
          </p>
        </fieldset>
      }
      @if (!staff && d.claim.status === 'AwaitingInfo') {
        <fieldset>
          <legend>The officer asked for more information</legend>
          <p>{{ openQuestion() }}</p>
          <label>Your answer <textarea #answer rows="3"></textarea></label>
          <button (click)="act('POST', 'provide-info', { answer: answer.value })">Send answer</button>
        </fieldset>
      }

      <h2>Timeline</h2>
      <ol class="timeline">
        @for (t of d.timeline; track $index) {
          <li>
            <time>{{ t.at | date: 'medium' }}</time> · <strong>{{ t.eventType }}</strong> by {{ nameOf(t.actorId) }}
            @if (t.oldValue || t.newValue) { · {{ t.oldValue ?? '–' }} → {{ t.newValue }} }
            @if (t.note) { <blockquote>{{ t.note }}</blockquote> }
          </li>
        }
      </ol>
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

  protected readonly mineToHandle = computed(() => {
    const c = this.detail()?.claim;
    return !!c && this.staff && c.assignedOfficerId === this.auth.user()?.id
      && (c.status === 'InReview' || c.status === 'AwaitingInfo');
  });
  protected readonly openQuestion = computed(() =>
    this.detail()?.timeline.filter(t => t.eventType === 'InfoRequested').at(-1)?.note);

  constructor() {
    this.load();
  }

  // shortcut: names come from the seeded list (no users endpoint); unknown ids show as "someone else".
  protected nameOf(id: string) {
    return SEED_USERS.find(u => u.id === id)?.name ?? 'someone else';
  }

  protected act(method: 'POST' | 'PUT', action: string, body: object = {}) {
    this.error.set('');
    this.http.request(method, `/api/claims/${this.id}/${action}`, { body }).subscribe({
      next: () => this.load(),
      error: e => this.error.set(problemMessage(e)),
    });
  }

  // shortcut: reloads after each action, no polling or push; see decisions.md (Frontend).
  private load() {
    this.http.get<ClaimDetail>(`/api/claims/${this.id}`).subscribe({
      next: d => this.detail.set(d),
      error: e => this.error.set(problemMessage(e)),
    });
  }
}
