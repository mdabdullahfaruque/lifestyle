import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { I18nStore, Locale } from 'i18n';
import { filter } from 'rxjs';

import { ShellStore } from './shell';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly shell = inject(ShellStore);
  protected readonly i18n = inject(I18nStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected term = '';

  /** Mirrors the URL, so the box still shows the query after a reload or a shared link. */
  protected readonly searching = signal(false);

  constructor() {
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => {
      const q = this.route.snapshot.queryParamMap.get('q') ?? '';
      this.term = q;
      this.searching.set(!!q);
    });
  }

  protected submitSearch(): void {
    const q = this.term.trim();
    // Search always lands on the marketplace grid — searching from a product page and staying
    // there would show results the page cannot render.
    void this.router.navigate(['/'], { queryParams: q ? { q } : {} });
  }

  protected clearSearch(): void {
    this.term = '';
    void this.router.navigate(['/'], { queryParams: {} });
  }

  protected switchLocale(code: string): void {
    this.i18n.setLocale(code as Locale);
  }
}
