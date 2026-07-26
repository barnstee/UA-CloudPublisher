namespace Opc.Ua.Cloud.Publisher
{
    using Microsoft.AspNetCore.Http;
    using System;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Enforces mandatory HTTP Basic authentication for the whole application.
    /// The expected username and password are read from the UA_CLOUD_PUBLISHER_USERNAME
    /// and UA_CLOUD_PUBLISHER_PASSWORD environment variables.
    /// </summary>
    public class BasicAuthMiddleware
    {
        public const string UsernameEnvVar = "PUBLISHER_USERNAME";
        public const string PasswordEnvVar = "PUBLISHER_PASSWORD";

        private const string Realm = "UA Cloud Publisher";

        private readonly RequestDelegate _next;
        private readonly byte[] _expectedUsername;
        private readonly byte[] _expectedPassword;

        public BasicAuthMiddleware(RequestDelegate next)
        {
            _next = next;

            string username = Environment.GetEnvironmentVariable(UsernameEnvVar);
            string password = Environment.GetEnvironmentVariable(PasswordEnvVar);

            _expectedUsername = Encoding.UTF8.GetBytes(username);
            _expectedPassword = Encoding.UTF8.GetBytes(password);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string authHeader = context.Request.Headers.Authorization;

            if (!string.IsNullOrEmpty(authHeader)
                && AuthenticationHeaderValue.TryParse(authHeader, out AuthenticationHeaderValue headerValue)
                && "Basic".Equals(headerValue.Scheme, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(headerValue.Parameter))
            {
                try
                {
                    string credentials = Encoding.UTF8.GetString(Convert.FromBase64String(headerValue.Parameter));
                    int separatorIndex = credentials.IndexOf(':');
                    if (separatorIndex >= 0)
                    {
                        string username = credentials.Substring(0, separatorIndex);
                        string password = credentials.Substring(separatorIndex + 1);

                        if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(username), _expectedUsername)
                         && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), _expectedPassword))
                        {
                            await _next(context).ConfigureAwait(false);
                            return;
                        }
                    }
                }
                catch (FormatException)
                {
                    // malformed Base64 credentials - fall through to challenge
                }
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = $"Basic realm=\"{Realm}\", charset=\"UTF-8\"";
        }
    }
}
