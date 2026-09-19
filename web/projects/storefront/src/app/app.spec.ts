import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_BASE_URL, SURFACE } from 'auth';
import { LOCALE_MESSAGES } from 'i18n';

import { App } from './app';
import { MEDIA_BASE_URL } from './media';
import { STOREFRONT_MESSAGES } from './messages';

/**
 * The root shell renders the header, the search box and the locale switch, and it reaches
 * `ShellStore -> CatalogService -> HttpClient` the moment it is created.
 *
 * These were the Angular scaffold's tests and still asserted "Hello, storefront" long after the
 * storefront grew a real shell, so they failed on every CI run and took the whole Frontend job
 * with them. Rewritten against what the component actually does.
 */
describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        // The shell's store fetches the category tree on construction; the testing backend
        // answers nothing, which is the point — no request escapes the test.
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://localhost' },
        { provide: MEDIA_BASE_URL, useValue: 'http://localhost/media' },
        { provide: LOCALE_MESSAGES, useValue: STOREFRONT_MESSAGES },
        { provide: SURFACE, useValue: 'buyer' as const },
      ],
    }).compileComponents();
  });

  it('creates the shell with its dependencies resolved', () => {
    const fixture = TestBed.createComponent(App);

    expect(fixture.componentInstance).toBeTruthy();
  });

  /**
   * Search lives in the header rather than behind an icon — a deliberate value-retail choice, and
   * the one piece of the shell a buyer is most likely to reach for first.
   */
  it('renders the header search box', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('form.search')).toBeTruthy();
    expect(compiled.querySelector('input[type="search"]')).toBeTruthy();
  });
});
