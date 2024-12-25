using BigMission.CommandTools;
using BigMission.Database;
using BigMission.TestHelpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace BigMission.ServiceHub.Security;

public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthSchemeOptions>
{
    private readonly IDbContextFactory<RedMist> dbFactory;

    private IConfiguration Config { get; }
    private IDateTimeHelper DateTimeHelper { get; }


    public ApiKeyAuthHandler(IOptionsMonitor<ApiKeyAuthSchemeOptions> options, ILoggerFactory logger, IDbContextFactory<RedMist> dbFactory,
        UrlEncoder encoder, IConfiguration config, IDateTimeHelper dateTimeHelper)
        : base(options, logger, encoder)
    {
        this.dbFactory = dbFactory;
        Config = config;
        DateTimeHelper = dateTimeHelper;
    }


    protected async override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for access to AllowAnonymous to allow for health checks
        var endpoint = Context.GetEndpoint();
        if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
        {
            var c = new[] { new Claim(ClaimTypes.Anonymous, string.Empty) };
            var ci = new ClaimsIdentity(c, nameof(ApiKeyAuthHandler));
            var t = new AuthenticationTicket(new ClaimsPrincipal(ci), Scheme.Name);
            return AuthenticateResult.Success(t);
        }

        if (!Request.Headers.ContainsKey(HeaderNames.Authorization))
        {
            return AuthenticateResult.Fail("Header Not Found.");
        }

        var token = Request.Headers[HeaderNames.Authorization].ToString();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.Fail("Invalid token");
        }

        var authData = KeyUtilities.DecodeToken(token.Remove(0, 7));
        var result = await ValidateToken(authData.appId, authData.apiKey);
        if (!result.isValid)
        {
            return AuthenticateResult.Fail(result.message);
        }

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, token) };

        var claimsIdentity = new ClaimsIdentity(claims, nameof(ApiKeyAuthHandler));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private async Task<(bool isValid, string message)> ValidateToken(Guid appId, string apiKey)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var key = db.ApiKeys.FirstOrDefault(k => k.ServiceId == appId && k.Key == apiKey);

        if (key == null)
        {
            return (false, "Token not found.");
        }

        if (key.Expires < DateTimeHelper.UtcNow)
        {
            return (false, "Token expired.");
        }

        return (true, string.Empty);
    }
}
