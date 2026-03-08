using Microsoft.AspNetCore.Identity;

namespace MyVideoArchive.Controllers.Api;

/// <summary>
/// Admin API for user management. Used by the Users tab on the Admin page.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = Constants.Roles.Administrator)]
public class AdminUsersApiController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public AdminUsersApiController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    /// <summary>
    /// Returns the currently logged-in user's id and email for read-only row protection.
    /// </summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Ok(new { success = false, message = "User not found" });
        }

        return Ok(new
        {
            success = true,
            data = new { user.Id, user.Email, user.UserName }
        });
    }

    /// <summary>
    /// Returns all application roles for the role dropdown.
    /// </summary>
    [HttpGet("/api/admin/roles")]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _roleManager.Roles
            .Select(r => new { r.Id, r.Name })
            .ToListAsync();
        return Ok(new { success = true, data = roles });
    }

    /// <summary>
    /// Returns all users with their roles and active status.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _userManager.Users.ToListAsync();
        var userList = new List<object>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            userList.Add(new
            {
                user.Id,
                user.UserName,
                user.Email,
                user.EmailConfirmed,
                user.LockoutEnabled,
                user.LockoutEnd,
                IsActive = user.LockoutEnd == null || user.LockoutEnd < DateTime.UtcNow,
                Roles = roles.ToList()
            });
        }

        return Ok(new { success = true, data = userList });
    }

    [HttpPost("")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return Ok(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });
        }

        if (!string.IsNullOrEmpty(request.Role))
        {
            await _userManager.AddToRoleAsync(user, request.Role);
        }

        return Ok(new { success = true, message = "User created successfully" });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserRequest request)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser != null && currentUser.Id == id)
        {
            return Ok(new { success = false, message = "You cannot edit your own account from this page." });
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return Ok(new { success = false, message = "User not found" });
        }

        user.Email = request.Email;
        user.UserName = request.Email;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Ok(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        if (!string.IsNullOrEmpty(request.Role))
        {
            await _userManager.AddToRoleAsync(user, request.Role);
        }

        return Ok(new { success = true, message = "User updated successfully" });
    }

    [HttpPost("{id}/toggle-status")]
    public async Task<IActionResult> ToggleUserStatus(string id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser != null && currentUser.Id == id)
        {
            return Ok(new { success = false, message = "You cannot disable your own account." });
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return Ok(new { success = false, message = "User not found" });
        }

        if (user.LockoutEnd == null || user.LockoutEnd < DateTime.UtcNow)
        {
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            return Ok(new { success = true, message = "User disabled successfully" });
        }

        await _userManager.SetLockoutEndDateAsync(user, null);
        return Ok(new { success = true, message = "User enabled successfully" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser != null && currentUser.Id == id)
        {
            return Ok(new { success = false, message = "You cannot delete your own account." });
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return Ok(new { success = false, message = "User not found" });
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return Ok(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });
        }

        return Ok(new { success = true, message = "User deleted successfully" });
    }

    public class CreateUserRequest
    {
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string? Role { get; set; }
    }

    public class UpdateUserRequest
    {
        public string Email { get; set; } = null!;
        public string? Role { get; set; }
    }
}