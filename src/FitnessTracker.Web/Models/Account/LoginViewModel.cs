using System.ComponentModel.DataAnnotations;

namespace FitnessTracker.Web.Models.Account;

public class LoginViewModel
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email address")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Keep me signed in on this device")]
    public bool RememberMe { get; set; }
}
