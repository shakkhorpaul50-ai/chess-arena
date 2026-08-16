using System.ComponentModel.DataAnnotations;

namespace WebApplication1.ViewModels;

public class RegisterViewModel
{
    [Required(ErrorMessage = "Display name is required.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Display name must be 3-32 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9_\- ]+$", ErrorMessage = "Display name may only contain letters, numbers, spaces, - and _.")]
    public string DisplayName { get; set; } = "";

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

public class LoginViewModel
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}