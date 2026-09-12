import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthStore } from 'auth';
import { BrandLogo } from 'ui';

import { ShopStore } from '../shop.store';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, BrandLogo],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  protected readonly auth = inject(AuthStore);
  protected readonly shop = inject(ShopStore);
  private readonly router = inject(Router);

  protected readonly menuOpen = signal(false);

  constructor() {
    // The whole console's shape depends on onboarding stage, so resolve it before any child
    // renders rather than letting each screen discover it separately.
    if (!this.shop.isLoaded()) void this.shop.refresh();
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    this.shop.clear();
    await this.router.navigateByUrl('/login');
  }
}
