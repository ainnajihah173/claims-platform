import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { Auth } from './auth';

// Adds the Bearer token to our own API calls only. A 401 means the token expired or was rejected: back to login.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(Auth);
  const router = inject(Router);
  const token = auth.user()?.token;
  if (token && req.url.startsWith('/api/')) {
    req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }
  return next(req).pipe(
    catchError((e: HttpErrorResponse) => {
      if (e.status === 401 && token) {
        auth.logout();
        router.navigateByUrl('/');
      }
      return throwError(() => e);
    }),
  );
};
