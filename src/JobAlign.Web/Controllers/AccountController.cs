using JobAlign.Core.Abstractions;
using JobAlign.Core.Entities.Identity;
using JobAlign.Web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace JobAlign.Web.Controllers;

/// <summary>
/// Registration, sign-in, sign-out and password reset (FR-01 to FR-05).
/// </summary>
/// <remarks>
/// Each action that must work signed out carries its own [AllowAnonymous]. A
/// class-level one would override the [Authorize] on Logout, so the opt-out is stated
/// per action instead. Everything without one is closed by the fallback policy in
/// Program.cs (NFR-04).
/// </remarks>
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ICandidateRegistrationService _registration;
    private readonly IAppEmailSender _email;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signIn,
        UserManager<ApplicationUser> users,
        ICandidateRegistrationService registration,
        IAppEmailSender email,
        ILogger<AccountController> logger)
    {
        _signIn = signIn;
        _users = users;
        _registration = registration;
        _email = email;
        _logger = logger;
    }

    // ---------------------------------------------------------------- FR-01

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    [HttpPost, ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _registration.RegisterAsync(
            model.Email, model.FullName, model.Password, cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return View(model);
        }

        _logger.LogInformation("Registered candidate {Email}.", model.Email);

        var user = await _users.FindByEmailAsync(model.Email);
        if (user is not null)
            await _signIn.SignInAsync(user, isPersistent: false);

        return RedirectToAction("Index", "Postings");
    }

    // ---------------------------------------------------------------- FR-02

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost, ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _users.FindByEmailAsync(model.Email);

        // FR-55: an account an administrator deactivated cannot sign in. Checked before
        // the password so a deactivated account does not accumulate lockout counters.
        if (user is { IsActive: false })
        {
            ModelState.AddModelError(string.Empty, "This account has been deactivated. Contact an administrator.");
            return View(model);
        }

        var result = await _signIn.PasswordSignInAsync(
            model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("User {Email} signed in.", model.Email);
            return RedirectToLocal(model.ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Account {Email} is locked out.", model.Email);
            ModelState.AddModelError(string.Empty,
                "This account is temporarily locked after too many failed attempts. Try again shortly.");
            return View(model);
        }

        // One message for "no such account" and "wrong password" alike: distinguishing
        // them tells an attacker which email addresses are registered.
        ModelState.AddModelError(string.Empty, "Incorrect email address or password.");
        return View(model);
    }

    // ---------------------------------------------------------------- FR-04

    [HttpPost, ValidateAntiForgeryToken, Authorize]   // no [AllowAnonymous]: signing out requires a session
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    // ---------------------------------------------------------------- FR-05

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _users.FindByEmailAsync(model.Email);

        // Always report the same outcome, whether or not the address is registered —
        // otherwise this form becomes a way to enumerate accounts.
        if (user is not null && user.IsActive)
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);

            var link = Url.Action(
                nameof(ResetPassword), "Account",
                new { email = model.Email, token },
                protocol: Request.Scheme);

            await _email.SendAsync(
                model.Email,
                "Reset your JobAlign password",
                $"Use this link to choose a new password:\n\n{link}\n\nIf you did not request this, ignore this message.",
                cancellationToken);
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPasswordConfirmation() => View();

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return BadRequest("This password reset link is incomplete.");

        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _users.FindByEmailAsync(model.Email);

        // Same reasoning as above: an unknown address must not be distinguishable
        // from a successful reset.
        if (user is null)
            return RedirectToAction(nameof(ResetPasswordConfirmation));

        var result = await _users.ResetPasswordAsync(user, model.Token, model.Password);

        if (result.Succeeded)
            return RedirectToAction(nameof(ResetPasswordConfirmation));

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPasswordConfirmation() => View();

    // ----------------------------------------------------------------

    /// <summary>
    /// Redirects only to paths inside this application. An unchecked returnUrl is an
    /// open redirect, which turns the sign-in page into a phishing aid.
    /// </summary>
    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Postings");
}
