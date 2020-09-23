using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;

namespace DeviceIdentityProvisioning.WebApp
{
    [Authorize]
    public class DeviceModel : PageModel
    {
        private const string DeviceDisplayNamePrefix = "Device";
        private readonly ILogger<DeviceModel> logger;
        public string NotificationMessage { get; set; }
        public ICollection<DeviceIdentity> Devices { get; set; }

        public DeviceModel(ILogger<DeviceModel> logger)
        {
            this.logger = logger;
        }

        public async Task OnGet(string notificationMessage)
        {
            this.NotificationMessage = notificationMessage;

            // Find all applications that have a display name that starts with the device prefix.
            // In real world scenarios, the actual device list would probably be stored and managed separately from Azure AD.
            var graphService = GetGraphServiceClientOnBehalfOfCurrentUser();
            var deviceApplicationRegistrations = await graphService.Applications.Request().Filter($"startswith(displayName,'{DeviceDisplayNamePrefix}')").GetAsync();
            this.Devices = deviceApplicationRegistrations.Select(d => new DeviceIdentity(d)).OrderByDescending(d => d.CreatedDateTime).ToArray();
        }

        public async Task<IActionResult> OnPost()
        {
            // Generate a unique display name with the prefix so that device identities can be easily retrieved by filtering on that prefix.
            var displayName = $"{DeviceDisplayNamePrefix} {Guid.NewGuid().ToString()}";
            var description = "Created by Device Identity Provisioning Service";
            var graphService = GetGraphServiceClientOnBehalfOfCurrentUser();
            await CreateDeviceIdentityAsync(graphService, displayName, description);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUseDeviceIdentityAsync(string id)
        {
            // Get the device and its details from Azure AD in this case.
            var graphService = GetGraphServiceClientOnBehalfOfCurrentUser();
            var deviceApplicationRegistration = await graphService.Applications[id].Request().GetAsync();
            var deviceIdentity = new DeviceIdentity(deviceApplicationRegistration);

            // Prove that we can actually USE the device identity to make a call to (in this case)
            // the Graph API using the permissions/roles that were granted.
            var notificationMessage = await CallApiUsingDeviceIdentityAsync(deviceIdentity);
            return RedirectToPage(new { notificationMessage = notificationMessage });
        }

        public async Task<IActionResult> OnPostDeleteDeviceIdentityAsync(string id)
        {
            var graphService = GetGraphServiceClientOnBehalfOfCurrentUser();
            await graphService.Applications[id].Request().DeleteAsync();
            return RedirectToPage();
        }

        private IGraphServiceClient GetGraphServiceClientOnBehalfOfCurrentUser()
        {
            // Create an instance of the Graph Service Client to access the Microsoft Graph API.
            // In real world scenarios, this would use MSAL to fetch the access token based on the currently
            // signed in user in the web app (optionally using the refresh token if it had expired etc.)
            // as documented at https://github.com/microsoftgraph/msgraph-sdk-dotnet-auth.
            // To simplify this sample, we just fetch the access token from the current user's claims to avoid
            // the complexity of an external token cache (see Startup.cs) and inject that token directly into
            // the Graph Service Client.
            var accessTokenClaim = this.User.Claims.Single(c => c.Type == Startup.ClaimTypeAccessToken);
            var authenticationProvider = new DelegateAuthenticationProvider(requestMessage =>
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessTokenClaim.Value);
                return Task.FromResult(0);
            });
            return new GraphServiceClient(authenticationProvider);
        }

        private async Task<DeviceIdentity> CreateDeviceIdentityAsync(IGraphServiceClient graphService, string displayName, string description)
        {
            // Get information about the customer tenant.
            var currentOrganization = (await graphService.Organization.Request().GetAsync()).Single();
            var currentTenantId = currentOrganization.Id;

            // Register an application representing the device.
            var deviceIdentityApplication = new Application
            {
                DisplayName = displayName,
                Description = description,
                SignInAudience = "AzureADMyOrg", // Limit exposure of this app registration to the customer tenant
                RequiredResourceAccess = new[]
                {
                    // Specify which permissions the device will ultimately need; in this case we use certain Graph API permissions to prove the point but this could be any Application Permission on any API.
                    new RequiredResourceAccess
                    {
                        ResourceAppId = "00000003-0000-0000-c000-000000000000", // Request that the device can access the Microsoft Graph API
                        ResourceAccess =  new []
                        {
                            new ResourceAccess { Type = "Role", Id = new Guid("798ee544-9d2d-430c-a058-570e29e34338") }, // Request an Application Permission (i.e. "Role") for the permission "Calendars.Read"
                            new ResourceAccess { Type = "Role", Id = new Guid("df021288-bdef-4463-88db-98f22de89214") }, // Request an Application Permission (i.e. "Role") for the permission "User.Read.All"
                        }
                    }
                }
            };
            deviceIdentityApplication = await graphService.Applications.Request().AddAsync(deviceIdentityApplication);
            this.logger.LogInformation($"Application created for device \"{deviceIdentityApplication.DisplayName}\": App ID = \"{deviceIdentityApplication.AppId}\"");
            await Task.Delay(1000); // Safety buffer

            // Create a client credential for the device.
            // https://docs.microsoft.com/en-us/graph/api/application-addpassword?view=graph-rest-1.0&tabs=http
            var clientCredential = await graphService.Applications[deviceIdentityApplication.Id].AddPassword(new PasswordCredential()).Request().PostAsync();
            this.logger.LogInformation($"Credential created for device: Client Secret = \"{clientCredential.SecretText}\"");
            await Task.Delay(1000); // Safety buffer

            // Store the client secret in the application so we can easily retrieve it later.
            // DO NOT EVER DO THIS IN PRODUCTION SCENARIOS OF COURSE!
            // This is simply to more easily demonstrate that the device can now effectively call the target API.
            // In a real world scenario, this step would handled e.g. through an IoT Hub where the actual identity
            // information including secrets are securely transmitted to the actual device.
            // Also add the Tenant Id to make it simpler to retrieve this when trying to use the device identity.
            var deviceIdentityApplicationPatch = new Application { Notes = $"{nameof(DeviceIdentity.TenantId)}={currentTenantId};{nameof(DeviceIdentity.ClientSecret)}={clientCredential.SecretText}" };
            await graphService.Applications[deviceIdentityApplication.Id].Request().UpdateAsync(deviceIdentityApplicationPatch);

            // Create the Service Principal for the device's app registration, as this will ultimately receive the required permissions (App Roles).
            var deviceIdentityServicePrincipal = await graphService.ServicePrincipals.Request().AddAsync(new ServicePrincipal { AppId = deviceIdentityApplication.AppId });
            this.logger.LogInformation($"Service Principal created for device: ID = \"{deviceIdentityApplication.Id}\"");
            await Task.Delay(1000); // Safety buffer

            // Perform an admin consent (i.e. add the App Role Assignment) using the customer tenant admin's privileges for each requested resource access in the device app registration.
            foreach (var requiredResourceAccess in deviceIdentityApplication.RequiredResourceAccess)
            {
                // Look up the Service Principal of the Resource AppId in the target tenant.
                var targetResourceServicePrincipal = (await graphService.ServicePrincipals.Request().Filter($"appId eq '{requiredResourceAccess.ResourceAppId}'").GetAsync()).Single();

                // Create the App Role Assignment for each requested resource.
                foreach (var appRole in requiredResourceAccess.ResourceAccess)
                {
                    var deviceAppRoleAssignment = new AppRoleAssignment
                    {
                        AppRoleId = appRole.Id,
                        PrincipalId = new Guid(deviceIdentityServicePrincipal.Id),
                        ResourceId = new Guid(targetResourceServicePrincipal.Id)
                    };
                    // https://docs.microsoft.com/en-us/graph/api/serviceprincipal-post-approleassignments?view=graph-rest-1.0&tabs=http
                    deviceAppRoleAssignment = await graphService.ServicePrincipals[deviceIdentityServicePrincipal.Id].AppRoleAssignments.Request().AddAsync(deviceAppRoleAssignment);
                }
            }

            return new DeviceIdentity
            {
                DisplayName = deviceIdentityApplication.DisplayName,
                Id = deviceIdentityApplication.Id,
                AppId = deviceIdentityApplication.AppId,
                TenantId = currentTenantId,
                ClientSecret = clientCredential.SecretText,
                CreatedDateTime = deviceIdentityApplication.CreatedDateTime
            };
        }

        private static async Task<string> CallApiUsingDeviceIdentityAsync(DeviceIdentity deviceIdentity)
        {
            try
            {
                var client = ConfidentialClientApplicationBuilder.Create(deviceIdentity.AppId)
                    .WithTenantId(deviceIdentity.TenantId)
                    .WithClientSecret(deviceIdentity.ClientSecret)
                    .Build();
                var graphService = new GraphServiceClient(new ClientCredentialProvider(client));
                var users = await graphService.Users.Request().GetAsync();
                return $"Successfully retrieved {users.Count} users from the Graph API using the identity of device \"{deviceIdentity.DisplayName}\" in tenant \"{deviceIdentity.TenantId}\", which demonstrates that the device is able to access the Graph API using its provisioned identity.";
            }
            catch (Exception exc)
            {
                return $"Failed to retrieve users from the Graph API using the identity of device \"{deviceIdentity.DisplayName}\" in tenant \"{deviceIdentity.TenantId}\": {exc.Message}.";
            }
        }
    }
}