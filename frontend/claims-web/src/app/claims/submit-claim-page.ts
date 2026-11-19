import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { problemMessage } from '../core/problem';
import { MARKET_NAMES } from '../shared/format';
import { ClaimDetail, ClaimType } from '../shared/models';

// Claimant only (route data.roles). `required` stops an empty form in the browser; the API still validates (400).
@Component({
  selector: 'app-submit-claim-page',
  imports: [RouterLink],
  template: `
    <a class="crumb" routerLink="/claims">← My claims</a>
    <div class="title-row"><h1>Submit a claim</h1></div>

    <form class="panel panel-body form" (submit)="submit($event, type.value, market.value, policy.value, description.value)">
      <div class="field">
        <label for="type">Type</label>
        <select id="type" #type required>
          @for (t of types; track t) { <option [value]="t">{{ t }}</option> }
        </select>
      </div>
      <div class="field">
        <label for="market">Market</label>
        <select id="market" #market required>
          @for (m of markets; track m.code) { <option [value]="m.code">{{ m.name }}</option> }
        </select>
      </div>
      <div class="field">
        <label for="policy">Policy number</label>
        <input id="policy" #policy required maxlength="50" />
      </div>
      <div class="field">
        <label for="description">What happened</label>
        <textarea id="description" #description required maxlength="2000" rows="4"></textarea>
      </div>
      @if (error()) { <p class="notice-error" role="alert">{{ error() }}</p> }
      <div><button class="btn btn-primary" [disabled]="busy()">{{ busy() ? 'Submitting…' : 'Submit claim' }}</button></div>
    </form>
  `,
  styles: `.form { display: flex; flex-direction: column; gap: 16px; max-width: 560px; }`,
})
export class SubmitClaimPage {
  private http = inject(HttpClient);
  private router = inject(Router);
  protected readonly types: ClaimType[] = ['Motor', 'Property'];
  protected readonly markets = Object.entries(MARKET_NAMES).map(([code, name]) => ({ code, name }));
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected submit(e: Event, type: string, market: string, policyNumber: string, description: string) {
    e.preventDefault();
    this.error.set('');
    this.busy.set(true);
    this.http.post<ClaimDetail>('/api/claims', { type, market, policyNumber, description })
      .subscribe({
        next: d => this.router.navigate(['/claims', d.claim.id]),
        error: err => {
          this.busy.set(false);
          // The rate limiter answers 429 with no body, so give it words here.
          this.error.set(err.status === 429 ? 'Too many claims in one minute. Please try again shortly.' : problemMessage(err));
        },
      });
  }
}
