import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Auth } from './auth';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: Auth;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(Auth);
    auth.user.set({ token: 't0k', id: '1', name: 'A', role: 'Claimant', exp: Date.now() / 1000 + 3600 });
  });

  it('sends the Bearer token to our API only', () => {
    http.get('/api/claims').subscribe();
    http.get('https://elsewhere.example/x').subscribe();
    expect(backend.expectOne('/api/claims').request.headers.get('Authorization')).toBe('Bearer t0k');
    expect(backend.expectOne('https://elsewhere.example/x').request.headers.has('Authorization')).toBe(false);
  });

  it('logs out on 401', () => {
    http.get('/api/claims').subscribe({ error: () => {} });
    backend.expectOne('/api/claims').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(auth.user()).toBeNull();
  });
});
