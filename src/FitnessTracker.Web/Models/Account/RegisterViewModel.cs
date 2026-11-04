using System.ComponentModel.DataAnnotations;

namespace FitnessTracker.Web.Models.Account;

public class RegisterViewModel
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email address")]
    public string Email { get; set; } = "";

    // Length and other rules live in Identity's options (Program.cs), so there is one source of truth.
    [Required(ErrorMessage = "Enter a password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Enter the password again.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The two passwords don't match.")]
    [Display(Name = "Password again")]
    public string ConfirmPassword { get; set; } = "";
}
