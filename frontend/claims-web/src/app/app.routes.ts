import { Routes } from '@angular/router';
import { ClaimDetailPage } from './claims/claim-detail-page';
import { ClaimListPage } from './claims/claim-list-page';
import { SubmitClaimPage } from './claims/submit-claim-page';
import { roleGuard } from './core/role.guard';
import { DashboardPage } from './dashboard/dashboard-page';
import { LoginPage } from './login/login-page';

export const routes: Routes = [
  { path: '', component: LoginPage },
  // Both screens are shared by claimants and staff, so no `data.roles` here; the guard only requires a login.
  { path: 'claims', component: ClaimListPage, canActivate: [roleGuard] },
  // Before claims/:id, or "new" would be read as an id.
  { path: 'claims/new', component: SubmitClaimPage, canActivate: [roleGuard], data: { roles: ['Claimant'] } },
  { path: 'claims/:id', component: ClaimDetailPage, canActivate: [roleGuard] },
  { path: 'dashboard', component: DashboardPage, canActivate: [roleGuard], data: { roles: ['Officer', 'Manager'] } },
  { path: '**', redirectTo: '' },
];
