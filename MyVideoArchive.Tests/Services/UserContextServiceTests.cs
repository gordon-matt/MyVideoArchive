using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace MyVideoArchive.Tests.Services;

public class UserContextServiceTests
{
    [Fact]
    public void GetCurrentUserId_ReturnsNameIdentifierClaim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-42")
        ], authenticationType: "test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });

        var service = new UserContextService(accessor.Object);

        Assert.Equal("user-42", service.GetCurrentUserId());
    }

    [Fact]
    public void GetCurrentUserId_WhenNoContext_ReturnsNull()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var service = new UserContextService(accessor.Object);

        Assert.Null(service.GetCurrentUserId());
    }

    [Fact]
    public void IsAdministrator_WhenUserInAdminRole_ReturnsTrue()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "admin"),
            new Claim(ClaimTypes.Role, Constants.Roles.Administrator)
        ], authenticationType: "test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });

        var service = new UserContextService(accessor.Object);

        Assert.True(service.IsAdministrator());
    }

    [Fact]
    public void IsAdministrator_WhenNoContext_ReturnsFalse()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var service = new UserContextService(accessor.Object);

        Assert.False(service.IsAdministrator());
    }
}