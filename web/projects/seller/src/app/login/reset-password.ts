import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthStore } from 'auth';
import { ProblemDetails } from 'data-access';
import { BrandLogo } from 'ui';

/** Mirrors Identity:MinimumPasswordLength. A mismatch here only produces a server-side rejection. */
const MINIMUM_LENGTH = 10;

@Component({
  selector: 'app-reset-password',
  imports: [FormsModule, RouterLink, BrandLogo],
  templateUrl: './reset-password.html',
  styleUrl: './login.scss',
})
export class ResetPassword {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  private readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token') ?? '';

  protected password = '';
  protected confirmation = '';

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** A link opened without a token is a truncated email, not a bug worth a stack trace. */
  protected readonly hasToken = this.token.length > 0;

  protected readonly minimumLength = MINIMUM_LENGTH;

  protected async submit(): Promise<void> {
    if (this.busy()) return;

    if (this.password.length < MINIMUM_LENGTH) {
      this.error.set(`Use at least ${MINIMUM_LENGTH} characters.`);
      return;
    }

    // Checked here rather than server-side: the server has no second field to compare, and a typo
    // in a password you cannot see is the single most likely way to be locked out again.
    if (this.password !== this.confirmation) {
      this.error.set('The two passwords do not match.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.auth.resetPassword(this.token, this.password);
      // Every session was revoked, including any this browser held. Sign-in is the only way on.
      await this.router.navigate(['/login'], { queryParams: { changed: '1' } });
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(false);
    }
  }

  private describe(err: unknown): string {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;

    if (problem?.code === 'identity.reset_token_invalid') {
      return 'That link has expired or has already been used. Request a new one.';
    }

    if (problem?.errors) return Object.values(problem.errors).flat().join(' ');

    return problem?.detail ?? 'Could not set the password just now. Please try again.';
  }
}
