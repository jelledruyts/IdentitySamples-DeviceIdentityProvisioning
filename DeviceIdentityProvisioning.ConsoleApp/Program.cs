using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;

namespace DeviceIdentityProvisioning.ConsoleApp
{
    class Program
    {
        private static readonly TimeSpan WaitPeriod = TimeSpan.FromSeconds(5); // Buffer time for Graph API calls.

        static async Task Main(string[] args)
        {
            // Load configuration.
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .AddUserSecrets<Program>(optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build();

            var deviceIdentityProvisioningAppId = configuration.GetValue<string>("AzureAd:ClientId");
            var lastCreatedDeviceIdentity = default(DeviceIdentity);
            while (true)
            {
                try
                {
                    Console.WriteLine("*** MAIN SCENARIO: Multi-tenant app with interactive user ***");
                    Console.WriteLine("A - Onboard a new customer tenant (optional)");
                    Console.WriteLine("B - Create a new device identity");
                    Console.WriteLine("C - Use the last created device identity");
                    Console.WriteLine();
                    Console.WriteLine("*** ALTERNATIVE SCENARIOS ***");
                    Console.WriteLine("D - Create a device identity from a single-tenant non-interactive provisioning app");
                    Console.WriteLine();
                    Console.Write("Type your choice and press Enter: ");
                    var choice = Console.ReadLine();
                    Console.WriteLine();
                    if (string.Equals(choice, "A", StringComparison.InvariantCultureIgnoreCase))
                    {
                        OnboardNewCustomerTenant(deviceIdentityProvisioningAppId);
                    }
                    else if (string.Equals(choice, "B", StringComparison.InvariantCultureIgnoreCase))
                    {
                        lastCreatedDeviceIdentity = await CreateDeviceIdentityInCustomerTenantAsync(deviceIdentityProvisioningAppId);
                    }
                    else if (string.Equals(choice, "C", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (lastCreatedDeviceIdentity != null)
                        {
                            var notificationMessage = await CallGraphApiUsingDeviceIdentityAsync(lastCreatedDeviceIdentity);
                            Console.WriteLine(notificationMessage);
                        }
                    }
                    else if (string.Equals(choice, "D", StringComparison.InvariantCultureIgnoreCase))
                    {
                        var tenantId = configuration.GetValue<string>("AzureAd:TenantId");
                        var deviceIdentityProvisioningAppSecret = configuration.GetValue<string>("AzureAd:ClientSecret");
                        var targetApiAppId = configuration.GetValue<string>("TargetApi:ClientId");
                        var targetApiRoleId = configuration.GetValue<string>("TargetApi:RoleId");
                        var targetApiScope = configuration.GetValue<string>("TargetApi:Scope");
                        await CreateDeviceIdentityFromProvisioningAppAsync(tenantId, deviceIdentityProvisioningAppId, deviceIdentityProvisioningAppSecret, targetApiAppId, targetApiRoleId, targetApiScope);
                    }
                    else
                    {
                        break;
                    }
                    Console.WriteLine();
                }
                catch (Exception exc)
                {
                    Console.WriteLine(exc.ToString());
                }
            }
        }

        private static void OnboardNewCustomerTenant(string deviceIdentityProvisioningAppId)
        {
            // See https://docs.microsoft.com/en-us/azure/active-directory/manage-apps/grant-admin-consent
            var adminConsentUrl = $"https://login.microsoftonline.com/common/adminconsent?client_id={deviceIdentityProvisioningAppId}";
            Console.WriteLine("Ask an administrator of the customer to navigate to the following URL and perform an admin consent in their tenant:");
            Console.WriteLine(adminConsentUrl);
        }

        private static async Task<DeviceIdentity> CreateDeviceIdentityInCustomerTenantAsync(string deviceIdentityProvisioningAppId)
        {
            // Create the MSAL public client application to allow the customer tenant administrator to sign in.
            // https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/System-Browser-on-.Net-Core
            var client = PublicClientApplicationBuilder.Create(deviceIdentityProvisioningAppId)
                .WithRedirectUri("http://localhost") // Required for MSAL
                .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs) // For multi-tenant applications (excluding consumer accounts)
                .Build();

            // Create the Graph Service client with an interactive authentication provider that uses the MSAL public client application.
            // Creating an Application or Service Principal using Delegated Permissions requires Application.ReadWrite.All, Directory.AccessAsUser.All (both require admin consent in the target tenant):
            // - https://docs.microsoft.com/en-us/graph/api/application-post-applications?view=graph-rest-1.0&tabs=http
            // - https://docs.microsoft.com/en-us/graph/api/serviceprincipal-post-serviceprincipals?view=graph-rest-1.0&tabs=http)
            // Note that these are the required permissions to create the device identity in the target tenant, which is unrelated from the final permissions the device will get.
            var scopes = new[] { "Application.ReadWrite.All", "Directory.AccessAsUser.All" };
            var graphService = new GraphServiceClient(new InteractiveAuthenticationProvider(client, scopes, Microsoft.Identity.Client.Prompt.SelectAccount));

            // Get information about the customer tenant.
            var currentOrganization = (await graphService.Organization.Request().GetAsync()).Single();
            var currentTenantId = currentOrganization.Id;

            // Specify which permissions the device will ultimately need.
            // In this case we use certain Graph API permissions to prove the point but this could be
            // any Application Permission on any API.
            var requiredResourceAccess = new[]
            {
                new RequiredResourceAccess
                {
                    // Request that the device can access the Microsoft Graph API.
                    ResourceAppId = "00000003-0000-0000-c000-000000000000",
                    ResourceAccess =  new []
                    {
                        // Request an Application Permission (i.e. "Role") for the permission "Calendars.Read".
                        new ResourceAccess { Type = "Role", Id = new Guid("798ee544-9d2d-430c-a058-570e29e34338") },
                         // Request an Application Permission (i.e. "Role") for the permission "User.Read.All".
                        new ResourceAccess { Type = "Role", Id = new Guid("df021288-bdef-4463-88db-98f22de89214") }
                    }
                }
            };

            return await CreateDeviceIdentityAsync(graphService, currentTenantId, requiredResourceAccess);
        }

        private static async Task CreateDeviceIdentityFromProvisioningAppAsync(string tenantId, string deviceIdentityProvisioningAppId, string deviceIdentityProvisioningAppSecret, string targetApiAppId, string targetApiRoleId, string targetApiScope)
        {
            // Create the MSAL confidential client application which has permissions to register device identities,
            // and is also an owner of the target API so it can perform the required admin consent (without being a
            // directory admin or otherwise having high-privilege permissions).
            // This means:
            // - The provisioning app must have "Application.ReadWrite.OwnedBy" permissions (not more), so that it
            //   can register the device identities (apps).
            // - The provisioning app also needs to be set as an owner of the target API, so that it can grant the
            //   admin consent on the target API for the client device identity without needing additional directory
            //   permissions.
            var client = ConfidentialClientApplicationBuilder.Create(deviceIdentityProvisioningAppId)
                .WithClientSecret(deviceIdentityProvisioningAppSecret)
                .WithTenantId(tenantId)
                .Build();
            var graphService = new GraphServiceClient(new ClientCredentialProvider(client));

            // Specify which permissions the device will ultimately need.
            // In this case we have to use a target API which we own (which excludes 3rd party multi-tenant apps
            // like the Graph API for example).
            var requiredResourceAccess = new[]
            {
                new RequiredResourceAccess
                {
                    // Request that the device can access the target API.
                    ResourceAppId = targetApiAppId,
                    ResourceAccess =  new []
                    {
                        // Request an Application Permission (i.e. "Role") for the required role.
                        new ResourceAccess { Type = "Role", Id = new Guid(targetApiRoleId) }
                    }
                }
            };

            // Create the device identity.
            var deviceIdentity = await CreateDeviceIdentityAsync(graphService, tenantId, requiredResourceAccess);
            await Task.Delay(WaitPeriod); // Safety buffer

            // Acquire a token to prove that everything worked.
            var accessToken = await GetTokenForDeviceIdentity(deviceIdentity, new[] { targetApiScope });
            Console.WriteLine($"Acquired token for device identity to call target API: {accessToken}");
        }

        private static async Task<DeviceIdentity> CreateDeviceIdentityAsync(GraphServiceClient graphService, string tenantId, IEnumerable<RequiredResourceAccess> requiredResourceAccess)
        {
            // Register an application representing the device.
            var deviceIdentityApplication = new Application
            {
                DisplayName = $"Device {Guid.NewGuid().ToString()}",
                Description = "Created by Device Identity Provisioning Service",
                SignInAudience = "AzureADMyOrg", // Limit exposure of this app registration to the customer tenant
                RequiredResourceAccess = requiredResourceAccess
            };
            deviceIdentityApplication = await graphService.Applications.Request().AddAsync(deviceIdentityApplication);
            Console.WriteLine($"Application created for device \"{deviceIdentityApplication.DisplayName}\": App ID = \"{deviceIdentityApplication.AppId}\"");
            await Task.Delay(WaitPeriod); // Safety buffer

            // Create a client credential for the device.
            // https://docs.microsoft.com/en-us/graph/api/application-addpassword?view=graph-rest-1.0&tabs=http
            var clientCredential = await graphService.Applications[deviceIdentityApplication.Id].AddPassword(new PasswordCredential()).Request().PostAsync();
            Console.WriteLine($"Credential created for device: Client Secret = \"{clientCredential.SecretText}\"");
            await Task.Delay(WaitPeriod); // Safety buffer

            // Create the Service Principal for the device's app registration, as this will ultimately receive the required permissions (App Roles).
            var deviceIdentityServicePrincipal = await graphService.ServicePrincipals.Request().AddAsync(new ServicePrincipal { AppId = deviceIdentityApplication.AppId });
            Console.WriteLine($"Service Principal created for device: ID = \"{deviceIdentityApplication.Id}\"");
            await Task.Delay(WaitPeriod); // Safety buffer

            // Perform an admin consent (i.e. add the App Role Assignment) for each requested resource access in the device app registration.
            foreach (var requiredResourceAccessInstance in deviceIdentityApplication.RequiredResourceAccess)
            {
                // Look up the Service Principal of the Resource AppId in the target tenant.
                var targetResourceServicePrincipal = (await graphService.ServicePrincipals.Request().Filter($"appId eq '{requiredResourceAccessInstance.ResourceAppId}'").GetAsync()).Single();

                // Create the App Role Assignment for each requested resource.
                foreach (var appRole in requiredResourceAccessInstance.ResourceAccess)
                {
                    var deviceAppRoleAssignment = new AppRoleAssignment
                    {
                        AppRoleId = appRole.Id,
                        PrincipalId = new Guid(deviceIdentityServicePrincipal.Id),
                        ResourceId = new Guid(targetResourceServicePrincipal.Id)
                    };
                    // https://docs.microsoft.com/en-us/graph/api/serviceprincipal-post-approleassignments?view=graph-rest-1.0&tabs=http
                    deviceAppRoleAssignment = await graphService.ServicePrincipals[deviceIdentityServicePrincipal.Id].AppRoleAssignments.Request().AddAsync(deviceAppRoleAssignment);
                    Console.WriteLine($"Device identity's App Role assigned and consented for target API \"{requiredResourceAccessInstance.ResourceAppId}\" and role ID \"{appRole.Id}\"");
                }
            }

            return new DeviceIdentity
            {
                DisplayName = deviceIdentityApplication.DisplayName,
                Id = deviceIdentityApplication.Id,
                AppId = deviceIdentityApplication.AppId,
                TenantId = tenantId,
                ClientSecret = clientCredential.SecretText,
                CreatedDateTime = deviceIdentityApplication.CreatedDateTime
            };
        }

        private static async Task<string> CallGraphApiUsingDeviceIdentityAsync(DeviceIdentity deviceIdentity)
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

        private static async Task<string> GetTokenForDeviceIdentity(DeviceIdentity deviceIdentity, IEnumerable<string> scopes)
        {
            var client = ConfidentialClientApplicationBuilder.Create(deviceIdentity.AppId)
                .WithTenantId(deviceIdentity.TenantId)
                .WithClientSecret(deviceIdentity.ClientSecret)
                .Build();
            var token = await client.AcquireTokenForClient(scopes).ExecuteAsync();
            return token.AccessToken;
        }

        private class DeviceIdentity
        {
            public string DisplayName { get; set; }
            public string Id { get; set; }
            public string AppId { get; set; }
            public string TenantId { get; set; }
            public string ClientSecret { get; set; }
            public DateTimeOffset? CreatedDateTime { get; set; }
        }
    }
}