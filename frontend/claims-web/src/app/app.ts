import { Component, HostListener, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { Auth } from './core/auth';
import { initialOf, personHue } from './shared/format';

@Component({
  imports: [RouterOutlet, RouterLink],
  selector: 'app-root',
  templateUrl: './app.html',
})
export class App {
  protected readonly auth = inject(Auth);
  private router = inject(Router);
  protected readonly scrolled = signal(false);
  protected readonly hue = personHue;
  protected readonly initial = initialOf;

  // The sticky bar shares the ground, so it gets its rule only once content scrolls under it.
  @HostListener('window:scroll')
  onScroll() {
    this.scrolled.set(window.scrollY > 0);
  }

  logout() {
    this.auth.logout();
    this.router.navigateByUrl('/');
  }
}
