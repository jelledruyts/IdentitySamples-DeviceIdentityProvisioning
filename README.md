# Identity Sample for Azure AD - Device Identity Provisioning

This repository contains a Visual Studio (Code) solution that demonstrates a multi-tenant solution for provisioning device identities in a customer's Azure AD tenant.

**IMPORTANT NOTE: The code in this repository is _not_ production-ready. It serves only to demonstrate the main points via minimal working code, and contains no exception handling or other special cases. Refer to the official documentation and samples for more information. Similarly, by design, it does not implement any caching or data persistence (e.g. to a database) to minimize the concepts and technologies being used.**

## Main Scenario: Interactive Admin Provisioning

This sample demonstrates a scenario where a device vendor sells devices to customers, and those devices should be able to access API's using an identity specifically provisioned for that device. This device identity and its permissions should be owned and controlled by the customer, not the vendor, as the device is ultimately accessing the API's and data _of the customer_.

More specifically, in this scenario the vendor hosts a central Device Identity Provisioning web application which allows customer administrators to sign in using their organizational Azure AD account. Using this multi-tenant web application, the customer can provision device identities in their own Azure AD tenant. In this sample they can also use the web application to call the Microsoft Graph API using the device identity (_not their own identity_) to demonstrate the end-to-end flow. Note that the use of the Microsoft Graph API here is irrelevant: it could be any API which supports Azure AD authentication.

### Permissions & Consent

When the customer admin signs in to the web application, they [consent](https://docs.microsoft.com/azure/active-directory/develop/consent-framework) to the app being able to register applications in their own Azure AD tenant _on the customer admin's behalf_: this requires `Application.ReadWrite.All` and `Directory.AccessAsUser.All` _delegated permissions_ on the Microsoft Graph API, which are [admin-only permissions](https://docs.microsoft.com/graph/permissions-reference) and is the first reason the user must be an admin in the customer tenant.

The app is then able to use the Microsoft Graph API (again _on behalf of_ the customer's admin) to [create the application registration](https://docs.microsoft.com/graph/api/application-post-applications?view=graph-rest-1.0&tabs=http) that represents the device, as well as the related [service principal](https://docs.microsoft.com/graph/api/serviceprincipal-post-serviceprincipals?view=graph-rest-1.0&tabs=http). Finally, the app then uses the customer admin's same permissions to grant that device service principal the required _application permissions_ (i.e. it [assigns an App Role](https://docs.microsoft.com/graph/api/serviceprincipal-post-approleassignments?view=graph-rest-1.0&tabs=http)) towards the target API. This ensures the device will act on behalf of itself (typically using an [OAuth 2.0 Client Credentials flow](https://docs.microsoft.com/azure/active-directory/develop/v2-oauth2-client-creds-grant-flow)), not on behalf of the customer's tenant admin. Note that [granting application permissions always requires admin consent](https://docs.microsoft.com/azure/active-directory/develop/v2-permissions-and-consent#permission-types), which is the second reason the user must be an admin.

In this sample, the target API that the device will connect to happens to be the Microsoft Graph API again, and to show two potential permissions it may need, we are granting it permissions to the `Calendars.Read` and `User.Read.All` roles in this case. This would allow the devices to read calendar information from all the users in the directory, as well as read user information.

At this point, the device can operate on its own in the customer tenant, without requiring anything else from the vendor tenant. Only if the customer admin needs to provision other devices, they would keep using the multi-tenant web app (which still has the consented permissions to do so on their behalf). If they do not wish to keep granting that highly privileged permission to the vendor's app, they can simply revoke their initial consent - but their devices would still continue to operate normally as they have no dependency on the vendor tenant.

### Setup

To run this sample successfully, complete the following steps:

- Create a **multi-tenant** app registration in your Azure AD for the provisioning app:
  - Under **Supported account types**, select **Accounts in any organizational directory (Any Azure AD directory - Multitenant)** to allow users from other Azure AD organizations to sign in to the application.
  - Set the **Redirect URI** to `https://localhost:5001/signin-oidc` when running the web app locally.
  - If you also want to use the console app, add a platform for **Mobile and desktop applications** and set the **Redirect URI** to `http://localhost`.
  - Allow the **Implicit grant** flow for **ID tokens**.
  - Create a **client secret** for the web app.
- Configure the app settings with all required values from the steps above:
  - I.e. take the correct values for the app client id and client secret and store them in the `appsettings.json` file or (preferred for local development) in [.NET User Secrets](https://docs.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-3.1&tabs=windows) or (preferred in cloud hosting platforms) through the appropriate app settings.
- Run the application and sign in with an admin user of **any** Azure AD tenant (typically _not_ the one where you registered the app which would be the vendor tenant in this scenario).

## Alternative Scenario: Non-Interactive Non-Admin Provisioning

The console app also demonstrates an alternative scenario, where device provisioning is only needed within a single organization (not multi-tenant), and where the provisioning and consent aren't done by an interactive user but by an app itself (workload identity). This could be an background automation job or a DevOps pipeline for example, using its own client credentials to perform the device registration.

In order for this provisioning app to have least privilege permissions, it should not be granted `Application.ReadWrite.All` or `AppRoleAssignment.ReadWrite.All` permissions, as these are highly privileged.

One approach is to [use app consent policies and custom directory roles to restrict the permissions which the provisioning app itself can grant to other identities](https://winsmarts.com/automating-application-permission-grant-while-avoiding-approleassignment-readwrite-all-554a83d5b6f5). This requires a Premium P1 or P2 license however, and is not available in Azure AD B2C.

As an alternative to app consent policies, it is possible to grant the provisioning app `Application.ReadWrite.OwnedBy` permissions _only_, so that it can register the device identities and manage only those that it has created itself. The provisioning app should also be configured as an owner on the target API's that the devices should be able to call, so that it can perform the required admin consent for that permission grant (again, without having over-privileged directory admin permissions).

The important constraint for this scenario is that it is only possible to grant the devices permissions to apps/APIs _which the organization itself has defined_ - i.e. no multi-tenant or 3rd party apps such as the Microsoft Graph API), because you have to be able to make the provisioning app an owner of the target APIs.

### Setup

To run this sample successfully (using the console app only), complete the following steps:

- You should already have one or more app registrations for the target APIs the devices should be able to call.
  - These should have [app roles](https://learn.microsoft.com/azure/active-directory/develop/howto-add-app-roles-in-azure-ad-apps) registered so that the devices can be granted permissions to it (which implies they include `Application` in the `allowedMemberTypes`).
- Create a **single-tenant** app registration in your Azure AD for the provisioning app (see below for automation):
  - Under **Supported account types**, select **Accounts in this organizational directory only**.
  - You can leave the redirect URI empty as this isn't needed for a client credentials flow.
  - Create a **client secret** for the app.
  - Configure API permissions for `Application.ReadWrite.OwnedBy` on the Microsoft Graph API and perform the required admin consent.
  - Make the provisioning app's service principal an owner of the target API's service principal; note that this cannot be done in the Portal today, so you'll need to do this via PowerShell (see below) or Graph API directly.
- Configure the app settings with all required values from the steps above:
  - I.e. take the correct values for the provisioning app's client id and client secret and store them in the `appsettings.json` file or (preferred for local development) in [.NET User Secrets](https://docs.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-3.1&tabs=windows) or (preferred in cloud hosting platforms) through the appropriate app settings.
  - For the target API, also store the client id, app role id and the full scope (for example, `https://mytenant.onmicrosoft.com/MyTargetApi/.default`).
- Run the console application.

```powershell
# Configure variables for your environment.
$TenantId = "<tenant-id>"
$TargetApiClientId = "<target-api-client-id>"

# Connect to Microsoft Graph with sufficient management permissions.
Connect-MgGraph -TenantId $TenantId -Scopes "Application.ReadWrite.All", "Directory.AccessAsUser.All", "Directory.ReadWrite.All"

# Create the provisioning app.
$ProvisioningApp = New-MgApplication -DisplayName ProvisioningApp -SignInAudience AzureADMyOrg
Write-Host "Client Id: $($ProvisioningApp.AppId)"
$ProvisioningAppSecret = Add-MgApplicationPassword -applicationId $ProvisioningApp.Id
Write-Host "Client Secret: $($ProvisioningAppSecret.SecretText)"
$ProvisioningSP = New-MgServicePrincipal -AppId $ProvisioningApp.AppId

# Declare API permissions for "Application.ReadWrite.OwnedBy" on the Microsoft Graph API.
$GraphAppId = "00000003-0000-0000-c000-000000000000"
$GraphAppRoleId = "18a4783c-866b-4cc7-a460-3d5e5662c884" # Application.ReadWrite.OwnedBy
$GraphAccess = New-Object -TypeName Microsoft.Graph.PowerShell.Models.MicrosoftGraphRequiredResourceAccess
$GraphAccess.ResourceAppId = $GraphAppId
$GraphAccess.ResourceAccess = @{ Id = $GraphAppRoleId; Type = "Role" }
Update-MgApplication -ApplicationId $ProvisioningApp.Id -RequiredResourceAccess @($GraphAccess)

# Grant admin consent for the "Application.ReadWrite.OwnedBy" permission.
$GraphSP = Get-MgServicePrincipal -Filter "AppId eq '$GraphAppId'"
New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $ProvisioningSP.Id -PrincipalId $ProvisioningSP.Id -ResourceId $GraphSP.Id -AppRoleId $GraphAppRoleId

# Make the provisioning app's service principal an owner of the target API's service principal.
$TargetApiSP = Get-MgServicePrincipal -Filter "AppId eq '$TargetApiClientId'"
New-MgServicePrincipalOwnerByRef -ServicePrincipalId $TargetApiSP.Id -OdataId "https://graph.microsoft.com/v1.0/directoryObjects/$($ProvisioningSP.Id)"
```
