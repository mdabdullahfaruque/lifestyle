using Lifestyle.Modules.Identity.Domain;
using Shouldly;
using Xunit;
using IdentityEvents = Lifestyle.Modules.Identity.Domain.Events;

namespace Lifestyle.UnitTests.Identity;

/// <summary>
/// Account security invariants (FRD §4.2). These are the rules that decide whether a stolen token
/// or a guessed password gets anywhere.
/// </summary>
public sealed class UserSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

    private static User NewUser() => User.Register("AISHA@Example.COM ", "hash", " Aisha Rahman ", "+60123456789", Now);

    [Fact]
    public void Registration_normalises_the_email_and_trims_the_name()
    {
        var user = NewUser();

        user.Email.ShouldBe("aisha@example.com");
        user.FullName.ShouldBe("Aisha Rahman");
        user.Status.ShouldBe(UserStatus.Active);
        user.DomainEvents.ShouldContain(e => e is IdentityEvents.UserRegistered);
    }

    [Fact]
    public void Five_failed_logins_lock_the_account_for_fifteen_minutes()
    {
        var user = NewUser();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            user.RecordFailedLogin(Now);
            user.IsLockedOut(Now).ShouldBeFalse($"attempt {attempt + 1} should not lock the account yet");
        }

        user.RecordFailedLogin(Now);

        user.IsLockedOut(Now).ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(14)).ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(16)).ShouldBeFalse();
    }

    [Fact]
    public void A_successful_login_clears_the_failure_count()
    {
        var user = NewUser();
        user.RecordFailedLogin(Now);
        user.RecordFailedLogin(Now);

        user.RecordSuccessfulLogin(Now);

        user.FailedLoginAttempts.ShouldBe(0);
        user.LastLoginAt.ShouldBe(Now);
        user.IsLockedOut(Now).ShouldBeFalse();
    }

    /// <summary>
    /// Changing a password is how someone reacts to a suspected compromise. If other sessions
    /// survived it, the change would achieve nothing.
    /// </summary>
    [Fact]
    public void Changing_the_password_revokes_every_active_session()
    {
        var user = NewUser();
        user.IssueRefreshToken("hash-a", Guid.CreateVersion7(), Now, TimeSpan.FromDays(30), "ua", "ip");
        user.IssueRefreshToken("hash-b", Guid.CreateVersion7(), Now, TimeSpan.FromDays(30), "ua", "ip");

        user.RefreshTokens.Count(t => t.IsActive(Now)).ShouldBe(2);

        user.SetPasswordHash("new-hash", Now);

        user.RefreshTokens.ShouldAllBe(t => !t.IsActive(Now));
        user.RefreshTokens.ShouldAllBe(t => t.RevokedReason == "password_changed");
    }

    /// <summary>
    /// Token reuse means the token leaked. Revoking only the replayed token would leave the thief's
    /// successor working, so the entire family goes.
    /// </summary>
    [Fact]
    public void Revoking_a_token_family_kills_every_token_in_the_chain()
    {
        var user = NewUser();
        var family = Guid.CreateVersion7();
        var otherFamily = Guid.CreateVersion7();

        user.IssueRefreshToken("hash-1", family, Now, TimeSpan.FromDays(30), null, null);
        user.IssueRefreshToken("hash-2", family, Now, TimeSpan.FromDays(30), null, null);
        user.IssueRefreshToken("hash-other", otherFamily, Now, TimeSpan.FromDays(30), null, null);

        user.RevokeTokenFamily(family, Now, "replay_detected");

        user.RefreshTokens.Where(t => t.FamilyId == family).ShouldAllBe(t => !t.IsActive(Now));
        user.RefreshTokens.First(t => t.FamilyId == otherFamily).IsActive(Now)
            .ShouldBeTrue("a different device's session is not affected");
    }

    [Fact]
    public void A_rotated_token_reads_as_replayed_if_presented_again()
    {
        var user = NewUser();
        var family = Guid.CreateVersion7();
        var original = user.IssueRefreshToken("hash-1", family, Now, TimeSpan.FromDays(30), null, null);
        var successor = user.IssueRefreshToken("hash-2", family, Now, TimeSpan.FromDays(30), null, null);

        original.MarkRotated(successor.Id, Now);

        original.IsReplayed(Now).ShouldBeTrue();
        original.ReplacedByTokenId.ShouldBe(successor.Id);
        successor.IsActive(Now).ShouldBeTrue();
    }

    [Fact]
    public void An_expired_token_reads_as_replayed()
    {
        var user = NewUser();
        var token = user.IssueRefreshToken("hash", Guid.CreateVersion7(), Now, TimeSpan.FromDays(30), null, null);

        token.IsActive(Now.AddDays(29)).ShouldBeTrue();
        token.IsActive(Now.AddDays(31)).ShouldBeFalse();
        token.IsReplayed(Now.AddDays(31)).ShouldBeTrue();
    }

    [Fact]
    public void Suspension_revokes_sessions_and_raises_an_event()
    {
        var user = NewUser();
        user.IssueRefreshToken("hash", Guid.CreateVersion7(), Now, TimeSpan.FromDays(30), null, null);

        user.Suspend(Now, "Fraudulent activity.");

        user.Status.ShouldBe(UserStatus.Suspended);
        user.RefreshTokens.ShouldAllBe(t => !t.IsActive(Now));
        user.DomainEvents.ShouldContain(e => e is IdentityEvents.UserSuspended);
    }

    [Fact]
    public void Changing_the_phone_number_clears_its_verified_flag()
    {
        var user = NewUser();
        user.UpdateProfile("Aisha Rahman", "+60123456789", Now);

        user.UpdateProfile("Aisha Rahman", "+60199999999", Now);

        user.PhoneVerified.ShouldBeFalse("a new number has not been proven to belong to this user");
    }

    [Fact]
    public void Roles_are_not_duplicated_within_the_same_scope()
    {
        var user = NewUser();
        var roleId = Guid.CreateVersion7();
        var vendorId = Guid.CreateVersion7();

        user.AssignRole(roleId, vendorId, Now);
        user.AssignRole(roleId, vendorId, Now);

        user.Roles.Count(r => r.RoleId == roleId && r.ScopeId == vendorId).ShouldBe(1);

        // The same role for a different vendor is a genuinely different grant.
        user.AssignRole(roleId, Guid.CreateVersion7(), Now);
        user.Roles.Count(r => r.RoleId == roleId).ShouldBe(2);
    }
}
