import { Routes } from '@angular/router';
import { ClaimDetailPage } from './claims/claim-detail-page';
import { ClaimListPage } from './claims/claim-list-page';
import { roleGuard } from './core/role.guard';
import { LoginPage } from './login/login-page';

export const routes: Routes = [
  { path: '', component: LoginPage },
  // Both screens are shared by claimants and staff, so no `data.roles` here; the guard only requires a login.
  { path: 'claims', component: ClaimListPage, canActivate: [roleGuard] },
  { path: 'claims/:id', component: ClaimDetailPage, canActivate: [roleGuard] },
  { path: '**', redirectTo: '' },
];
