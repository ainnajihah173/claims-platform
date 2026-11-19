import { Component, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { Auth } from './core/auth';

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  templateUrl: './app.html',
})
export class App {
  protected readonly auth = inject(Auth);
  private router = inject(Router);

  logout() {
    this.auth.logout();
    this.router.navigateByUrl('/');
  }
}
