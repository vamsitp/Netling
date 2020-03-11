﻿namespace Netling.Core
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Linq;
    using System.Security;
    using System.Threading.Tasks;

    using Microsoft.Identity.Client;
    using Microsoft.IdentityModel.Clients.ActiveDirectory;
    using Newtonsoft.Json;

    using AuthenticationResult = Microsoft.Identity.Client.AuthenticationResult;

    public class AuthHelper
    {
        public static string InputFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Netling.json");
        internal const string ClientId = "872cd9fa-d31f-45e0-9eab-6e460a02d1f1";    // Change to your app registration's Application ID, unless you are an MSA backed account
        internal const string ReplyUri = "urn:ietf:wg:oauth:2.0:oob";               // Change to your app registration's reply URI, unless you are an MSA backed account
        internal const string ResourceId = "https://management.azure.com"; // Constant value to target Azure DevOps. Do not change

        internal static readonly ConcurrentDictionary<string, Lazy<Task<string>>> AuthTokens = new ConcurrentDictionary<string, Lazy<Task<string>>>();

        public static async Task<string> GetAuthTokenSilentAsync(AuthInfo input)
        {
            var app = PublicClientApplicationBuilder.Create(input.ClientId).WithAuthority(input.Authority).Build();
            var accounts = await app.GetAccountsAsync().ConfigureAwait(continueOnCapturedContext: false);
            AuthenticationResult result = null;
            if (accounts?.Any() ?? false)
            {
                result = await app.AcquireTokenSilent(input.ApiScopes, accounts.FirstOrDefault()).ExecuteAsync();
            }
            else
            {
                try
                {
                    var securePassword = new SecureString();
                    var pwd = input.Password;
                    foreach (char c in pwd)
                    {
                        securePassword.AppendChar(c);
                    }
                    result = await app.AcquireTokenByUsernamePassword(input.ApiScopes, input.UserId, securePassword).ExecuteAsync();
                }
                catch (MsalException mex)
                {
                    Console.WriteLine(mex.Message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            Console.WriteLine($"\n{result.Account.Username}\n");
            return result.AccessToken;
        }

        public static async Task<string> GetAuthToken(string tenantId)
        {
            var accessToken = await AuthTokens.GetOrAdd(tenantId ?? string.Empty, k =>
            {
                return new Lazy<Task<string>>(async () =>
                {
                    var ctx = GetAuthenticationContext(tenantId);
                    Microsoft.IdentityModel.Clients.ActiveDirectory.AuthenticationResult result = null;
                    var promptBehavior = new PlatformParameters(PromptBehavior.SelectAccount, new CustomWebUi());
                    Console.Write("Authenticating...\n");
                    try
                    {
                        result = await ctx.AcquireTokenAsync(ResourceId, ClientId, new Uri(ReplyUri), promptBehavior);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // If the token has expired, prompt the user with a login prompt
                        result = await ctx.AcquireTokenAsync(ResourceId, ClientId, new Uri(ReplyUri), promptBehavior);
                    }

                    return result?.AccessToken;
                });
            }).Value;

            return accessToken;
        }

        private static AuthenticationContext GetAuthenticationContext(string tenant)
        {
            AuthenticationContext ctx = null;
            if (!string.IsNullOrWhiteSpace(tenant))
            {
                ctx = new AuthenticationContext("https://login.microsoftonline.com/" + tenant);
            }
            else
            {
                ctx = new AuthenticationContext("https://login.windows.net/common");
                if (ctx.TokenCache.Count > 0)
                {
                    string homeTenant = ctx.TokenCache.ReadItems().First().TenantId;
                    ctx = new AuthenticationContext("https://login.microsoftonline.com/" + homeTenant);
                }
            }

            return ctx;
        }
    }
}
