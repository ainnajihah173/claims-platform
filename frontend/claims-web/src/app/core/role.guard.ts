import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Role } from '../shared/models';
import { Auth } from './auth';

// Must be logged in; if the route lists `data.roles`, the user's role must be one of them.
// UX only: the API enforces the same rules (StaffOnly / ClaimantOnly + ownership).
export const roleGuard: CanActivateFn = route => {
  const user = inject(Auth).user();
  const roles = route.data['roles'] as Role[] | undefined;
  return user && (!roles || roles.includes(user.role)) ? true : inject(Router).createUrlTree(['/']);
};
