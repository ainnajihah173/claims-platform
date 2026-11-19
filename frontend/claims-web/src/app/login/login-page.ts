import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Auth, SEED_USERS } from '../core/auth';
import { problemMessage } from '../core/problem';
import { initialOf, personHue } from '../shared/format';
import { Role } from '../shared/models';

const FLOW = [
  { step: 'Submitted', what: 'A claimant files a motor or property claim against their policy.' },
  { step: 'In review', what: 'An officer picks it up and sets a reserve: the money held for it.' },
  { step: 'Awaiting info', what: 'The officer asks a question; the claimant’s answer sends it back to review.' },
  { step: 'Settled or rejected', what: 'The officer decides. A settlement can’t be more than the reserve.' },
];

const GROUPS: { role: Role; title: string; does: string }[] = [
  { role: 'Claimant', title: 'Claimants', does: 'Follow their own claims and answer the officer’s questions.' },
  { role: 'Officer', title: 'Claims officers', does: 'Pick up claims, ask for information, set reserves, settle or reject.' },
  { role: 'Manager', title: 'Manager', does: 'Sees every claim and can handle them like an officer.' },
];

@Component({
  selector: 'app-login-page',
  template: `
    <div class="login">
      <section class="intro">
        <h1>Sign in to Claims</h1>
        <p class="lead">Motor and property claims across Malaysia, Singapore, Hong Kong, Australia, Thailand and Indonesia.</p>
        <p class="muted">Development login: choose a seeded user. There is no password, but the API still issues
          and checks a signed token, so each user only sees and does what their role allows.</p>
        <h2 class="flow-head">How a claim moves</h2>
        <ol class="flow">
          @for (f of flow; track f.step) {
            <li><span class="flow-step">{{ f.step }}</span><span class="muted">{{ f.what }}</span></li>
          }
        </ol>
      </section>

      <section class="panel picker" aria-label="Choose a user">
        @for (g of groups; track g.role) {
          <div class="group">
            <h2>{{ g.title }}</h2>
            <p class="muted">{{ g.does }}</p>
            <ul>
              @for (u of usersFor(g.role); track u.id) {
                <li>
                  <button class="user" (click)="login(u.id)" [disabled]="busy() !== null">
                    <span class="avatar lg" [style.--h]="hue(u.id)" aria-hidden="true">{{ initial(u.id) }}</span>
                    <span class="who">{{ u.name }}</span>
                    <span class="go muted">{{ busy() === u.id ? 'Signing in…' : 'Sign in' }}</span>
                  </button>
                </li>
              }
            </ul>
          </div>
        }
        @if (error()) { <p class="notice-error" role="alert">{{ error() }}</p> }
      </section>
    </div>
  `,
  styles: `
    .login { display: grid; grid-template-columns: 1fr 1fr; gap: 48px; align-items: start; padding-top: 32px; }
    .intro { display: flex; flex-direction: column; gap: 16px; max-width: 460px; padding-top: 8px; }
    h1 { font-size: 30px; line-height: 36px; font-weight: 700; letter-spacing: -0.02em; }
    .lead { font-size: 18px; line-height: 26px; }
    .flow-head { font-size: var(--fs-section); font-weight: 500; line-height: 24px; margin-top: 16px; }
    .flow { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 14px; }
    .flow li { display: flex; flex-direction: column; gap: 2px; padding-left: 16px; border-left: 4px solid var(--text); }
    .flow li:last-child { border-left-color: var(--hue); }
    .flow-step { font-weight: 500; }
    .picker { padding: 8px 0; }
    .group { padding: 16px 20px 12px; }
    .group + .group { border-top: 1px solid var(--rule); }
    .group h2 { font-size: var(--fs-section); font-weight: 500; line-height: 24px; }
    .group p { margin-bottom: 8px; }
    ul { list-style: none; margin: 0; padding: 0; }
    .user {
      display: flex; align-items: center; gap: 12px; padding: 8px; margin: 0 -8px; width: calc(100% + 16px);
      border: 0; border-radius: var(--radius-control); background: none; cursor: pointer; text-align: left; min-height: 52px;
    }
    .user:hover:not(:disabled) { background: var(--active); }
    .user:hover .go { color: var(--text); }
    .user:disabled { cursor: progress; }
    .who { flex: 1; font-weight: 500; font-size: 15px; }
    .notice-error { margin: 8px 20px 12px; }
    @media (max-width: 900px) {
      .login { grid-template-columns: 1fr; gap: 24px; padding-top: 8px; }
      h1 { font-size: 24px; line-height: 30px; }
      .lead { font-size: 16px; line-height: 24px; }
    }
  `,
})
export class LoginPage {
  private auth = inject(Auth);
  private router = inject(Router);
  protected readonly groups = GROUPS;
  protected readonly flow = FLOW;
  protected readonly error = signal('');
  protected readonly busy = signal<string | null>(null);
  protected readonly hue = personHue;
  protected readonly initial = initialOf;

  protected usersFor(role: Role) {
    return SEED_USERS.filter(u => u.role === role);
  }

  login(id: string) {
    this.error.set('');
    this.busy.set(id);
    this.auth.login(id).subscribe({
      next: () => this.router.navigateByUrl('/claims'),
      error: e => { this.busy.set(null); this.error.set(problemMessage(e)); },
    });
  }
}
