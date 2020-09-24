using System.Linq;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;

namespace DeviceIdentityProvisioning.WebApp
{
    public static class GraphServiceClientFactory
    {
        public static IGraphServiceClient GetForUserIdentity(ClaimsPrincipal user)
        {
            // Create an instance of the Graph Service Client to access the Microsoft Graph API.
            // In real world scenarios, this would use MSAL to fetch the access token based on the currently
            // signed in user in the web app (optionally using the refresh token if it had expired etc.)
            // as documented at https://github.com/microsoftgraph/msgraph-sdk-dotnet-auth.
            // To simplify this sample, we just fetch the access token from the current user's claims to avoid
            // the complexity of an external token cache (see Startup.cs) and inject that token directly into
            // the Graph Service Client.
            var accessTokenClaim = user.Claims.Single(c => c.Type == Startup.ClaimTypeAccessToken);
            var authenticationProvider = new DelegateAuthenticationProvider(requestMessage =>
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessTokenClaim.Value);
                return Task.FromResult(0);
            });
            return new GraphServiceClient(authenticationProvider);
        }

        public static IGraphServiceClient GetForDeviceIdentity(DeviceIdentity deviceIdentity)
        {
            var client = ConfidentialClientApplicationBuilder.Create(deviceIdentity.AppId)
                .WithTenantId(deviceIdentity.TenantId)
                .WithClientSecret(deviceIdentity.ClientSecret)
                .Build();
            return new GraphServiceClient(new ClientCredentialProvider(client));
        }
    }
}