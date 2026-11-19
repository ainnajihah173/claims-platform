import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { map } from 'rxjs';
import { DevLoginResponse, Role } from '../shared/models';

// shortcut: seeded users hard-coded with the fixed ids from Seed.cs (there is no users endpoint);
// the picker goes away when real OIDC login replaces dev-login.
export const SEED_USERS: { id: string; name: string; role: Role }[] = [
  { id: '00000000-0000-0000-0000-000000000001', name: 'Aisha Claimant', role: 'Claimant' },
  { id: '00000000-0000-0000-0000-000000000002', name: 'Ben Claimant', role: 'Claimant' },
  { id: '00000000-0000-0000-0000-000000000011', name: 'Chen Officer', role: 'Officer' },
  { id: '00000000-0000-0000-0000-000000000012', name: 'Devi Officer', role: 'Officer' },
  { id: '00000000-0000-0000-0000-000000000013', name: 'Eric Officer', role: 'Officer' },
  { id: '00000000-0000-0000-0000-000000000021', name: 'Farah Manager', role: 'Manager' },
];

export interface Session {
  token: string;
  id: string;
  name: string;
  role: Role;
  exp: number; // seconds since epoch, from the token
}

const KEY = 'session';

@Injectable({ providedIn: 'root' })
export class Auth {
  private http = inject(HttpClient);

  readonly user = signal<Session | null>(restore());
  readonly isStaff = computed(() => ['Officer', 'Manager'].includes(this.user()?.role ?? ''));

  login(userId: string) {
    return this.http.post<DevLoginResponse>('/api/auth/dev-login', { userId }).pipe(
      map(r => {
        const s = toSession(r.token);
        // shortcut: token in sessionStorage is readable by any script on the page (XSS); use an
        // HttpOnly cookie or a BFF with real OIDC.
        sessionStorage.setItem(KEY, JSON.stringify(s));
        this.user.set(s);
        return s;
      }),
    );
  }

  logout() {
    sessionStorage.removeItem(KEY);
    this.user.set(null);
  }
}

// The server decides what a token allows; the UI only reads sub/name/role/exp to choose what to show.
function toSession(token: string): Session {
  const p = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
  return { token, id: p.sub, name: p.name, role: p.role, exp: p.exp };
}

function restore(): Session | null {
  const s: Session | null = JSON.parse(sessionStorage.getItem(KEY) ?? 'null');
  return s && s.exp * 1000 > Date.now() ? s : null;
}
