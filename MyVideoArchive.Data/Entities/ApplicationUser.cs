using Microsoft.AspNetCore.Identity;

namespace MyVideoArchive.Data.Entities;

public class ApplicationUser : IdentityUser
{
    public virtual ICollection<ApplicationRole> Roles { get; set; } = [];
}