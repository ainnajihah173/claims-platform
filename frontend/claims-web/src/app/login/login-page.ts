import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Auth, SEED_USERS } from '../core/auth';
import { problemMessage } from '../core/problem';

@Component({
  selector: 'app-login-page',
  template: `
    <h1>Log in as</h1>
    <ul class="users">
      @for (u of users; track u.id) {
        <li><button (click)="login(u.id)">{{ u.name }} <small>({{ u.role }})</small></button></li>
      }
    </ul>
    @if (error()) { <p class="error">{{ error() }}</p> }
  `,
})
export class LoginPage {
  private auth = inject(Auth);
  private router = inject(Router);
  protected readonly users = SEED_USERS;
  protected readonly error = signal('');

  login(id: string) {
    this.auth.login(id).subscribe({
      next: () => this.router.navigateByUrl('/claims'),
      error: e => this.error.set(problemMessage(e)),
    });
  }
}
