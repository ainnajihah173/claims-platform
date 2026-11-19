import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, signal, WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Auth } from '../core/auth';
import { problemMessage } from '../core/problem';
import { ClaimSummary } from '../shared/models';

// One list for both roles. queue=Mine means "assigned to me" for staff and "submitted by me" for claimants;
// staff also get the unassigned queue. Age and overdue are staff-only (the target is an internal SLA).
@Component({
  selector: 'app-claim-list-page',
  imports: [RouterLink, DatePipe, DecimalPipe, NgTemplateOutlet],
  template: `
    @if (error()) { <p class="error">{{ error() }}</p> }
    @if (staff) {
      <h2>Unassigned queue</h2>
      <ng-container *ngTemplateOutlet="table; context: { $implicit: queue() }" />
    }
    <h2>My claims</h2>
    <ng-container *ngTemplateOutlet="table; context: { $implicit: mine() }" />

    <ng-template #table let-claims>
      @if (claims.length === 0) {
        <p>None.</p>
      } @else {
        <table>
          <thead>
            <tr>
              <th>Submitted</th><th>Type</th><th>Market</th><th>Status</th><th>Reserve</th>
              @if (staff) { <th>Age (days)</th> }
            </tr>
          </thead>
          <tbody>
            @for (c of claims; track c.id) {
              <tr>
                <td><a [routerLink]="['/claims', c.id]">{{ c.submittedAt | date: 'mediumDate' }}</a></td>
                <td>{{ c.type }}</td>
                <td>{{ c.market }}</td>
                <td>{{ c.status }}</td>
                <td>{{ c.reserve | number: '1.2-2' }} {{ c.currency }}</td>
                @if (staff) {
                  <td>{{ c.ageDays }} @if (c.isOverdue) { <span title="Overdue">🔴</span> }</td>
                }
              </tr>
            }
          </tbody>
        </table>
      }
    </ng-template>
  `,
})
export class ClaimListPage {
  private http = inject(HttpClient);
  protected readonly staff = inject(Auth).isStaff();

  protected readonly mine = signal<ClaimSummary[]>([]);
  protected readonly queue = signal<ClaimSummary[]>([]);
  protected readonly error = signal('');

  // shortcut: loaded once per visit, no polling; re-open the page to refresh (see decisions.md, Frontend).
  constructor() {
    this.load('Mine', this.mine);
    if (this.staff) this.load('Unassigned', this.queue);
  }

  private load(queue: string, into: WritableSignal<ClaimSummary[]>) {
    this.http.get<ClaimSummary[]>(`/api/claims?queue=${queue}`).subscribe({
      next: r => into.set(r),
      error: e => this.error.set(problemMessage(e)),
    });
  }
}
