using BigMission.CommandTools;
using BigMission.Database;
using BigMission.TestHelpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace BigMission.ServiceHub.Security
{
    public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthSchemeOptions>
    {
        private IConfiguration Config { get; }
        private IDateTimeHelper DateTimeHelper { get; }


        public ApiKeyAuthHandler(IOptionsMonitor<ApiKeyAuthSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock, IConfiguration config, IDateTimeHelper dateTimeHelper)
            : base(options, logger, encoder, clock)
        {
            Config = config;
            DateTimeHelper = dateTimeHelper;
        }


        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(HeaderNames.Authorization))
            {
                return Task.FromResult(AuthenticateResult.Fail("Header Not Found."));
            }

            var token = Request.Headers[HeaderNames.Authorization].ToString();
            if (string.IsNullOrEmpty(token))
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid token"));
            }

            var authData = KeyUtilities.DecodeToken(token.Remove(0, 7));
            var result = ValidateToken(authData.appId, authData.apiKey);
            if (!result.isValid)
            {
                return Task.FromResult(AuthenticateResult.Fail(result.message));
            }

            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, token) };

            var claimsIdentity = new ClaimsIdentity(claims, nameof(ApiKeyAuthHandler));
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        private (bool isValid, string message) ValidateToken(Guid appId, string apiKey)
        {
            using var db = new RedMist(Config["ConnectionString"]);
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
}
