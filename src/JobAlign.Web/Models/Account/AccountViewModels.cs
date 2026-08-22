using System.ComponentModel.DataAnnotations;

namespace JobAlign.Web.Models.Account;

/// <summary>Registration form (FR-01).</summary>
public class RegisterViewModel
{
    /// <summary>
    /// Doubles as the username. FR-01 requires it to be unique; Identity is configured
    /// with RequireUniqueEmail and the normalized-email index enforces it in the database.
    /// </summary>
    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required, Display(Name = "Full name"), StringLength(128)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Minimum length mirrors the policy set in Program.cs; Identity is the authority.</summary>
    [Required, DataType(DataType.Password), StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>Sign-in form (FR-02).</summary>
public class LoginViewModel
{
    [Required, EmailAddress]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Stay signed in on this device")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

/// <summary>Start of the password reset flow (FR-05).</summary>
public class ForgotPasswordViewModel
{
    [Required, EmailAddress]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;
}

/// <summary>Completion of the password reset flow (FR-05).</summary>
public class ResetPasswordViewModel
{
    [Required, EmailAddress]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Identity's reset token, carried from the emailed link.</summary>
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(128, MinimumLength = 8)]
    [Display(Name = "New password")]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
