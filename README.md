# Identity Sample for Azure AD - Device Identity Provisioning

This repository contains a Visual Studio (Code) solution that demonstrates a multi-tenant solution for provisioning device identities in a customer's Azure AD tenant.

**IMPORTANT NOTE: The code in this repository is _not_ production-ready. It serves only to demonstrate the main points via minimal working code, and contains no exception handling or other special cases. Refer to the official documentation and samples for more information. Similarly, by design, it does not implement any caching or data persistence (e.g. to a database) to minimize the concepts and technologies being used.**

## Scenario

This sample demonstrates a scenario where a device vendor sells devices to customers, and those devices should be able to access API's using an identity specifically provisioned for that device. This device identity and its permissions should be owned and controlled by the customer, not the vendor, as the device is ultimately accessing the API's and data _of the customer_.

More specifically, in this scenario the vendor hosts a central Device Identity Provisioning web application which allows customer administrators to sign in using their organizational Azure AD account. Using this multi-tenant web application, the customer can provision device identities in their own Azure AD tenant. In this sample they can also use the web application to call the Microsoft Graph API using the device identity (_not their own identity_) to demonstrate the end-to-end flow. Note that the use of the Microsoft Graph API here is irrelevant: it could be any API which supports Azure AD authentication.

## Permissions & Consent

When the customer admin signs in to the web application, they [consent](https://docs.microsoft.com/azure/active-directory/develop/consent-framework) to the app being able to register applications in their own Azure AD tenant _on the customer admin's behalf_: this requires `Application.ReadWrite.All` and `Directory.AccessAsUser.All` _delegated permissions_ on the Microsoft Graph API, which are [admin-only permissions](https://docs.microsoft.com/graph/permissions-reference) and is the first reason the user must be an admin in the customer tenant.

The app is then able to use the Microsoft Graph API (again _on behalf of_ the customer's admin) to [create the application registration](https://docs.microsoft.com/graph/api/application-post-applications?view=graph-rest-1.0&tabs=http) that represents the device, as well as the related [service principal](https://docs.microsoft.com/graph/api/serviceprincipal-post-serviceprincipals?view=graph-rest-1.0&tabs=http). Finally, the app then uses the customer admin's same permissions to grant that device service principal the required _application permissions_ (i.e. it [assigns an App Role](https://docs.microsoft.com/graph/api/serviceprincipal-post-approleassignments?view=graph-rest-1.0&tabs=http)) towards the target API. This ensures the device will act on behalf of itself (typically using an [OAuth 2.0 Client Credentials flow](https://docs.microsoft.com/azure/active-directory/develop/v2-oauth2-client-creds-grant-flow)), not on behalf of the customer's tenant admin. Note that [granting application permissions always requires admin consent](https://docs.microsoft.com/azure/active-directory/develop/v2-permissions-and-consent#permission-types), which is the second reason the user must be an admin.

In this sample, the target API that the device will connect to happens to be the Microsoft Graph API again, and to show two potential permissions it may need, we are granting it permissions to the `Calendars.Read` and `User.Read.All` roles in this case. This would allow the devices to read calendar information from all the users in the directory, as well as read user information.

At this point, the device can operate on its own in the customer tenant, without requiring anything else from the vendor tenant. Only if the customer admin needs to provision other devices, they would keep using the multi-tenant web app (which still has the consented permissions to do so on their behalf). If they do not wish to keep granting that highly privileged permission to the vendor's app, they can simply revoke their initial consent - but their devices would still continue to operate normally as they have no dependency on the vendor tenant.

## Setup

To run this sample successfully, complete the following steps:

- Create a **multi-tenant** app registration in your Azure AD:
  - Under **Supported account types**, select **Accounts in any organizational directory (Any Azure AD directory - Multitenant)** to allow users from other Azure AD organizations to sign in to the application.
  - Set the **Redirect URI** to `https://localhost:5001/signin-oidc` when running the web app locally.
  - If you also want to use the console app, add a platform for **Mobile and desktop applications** and set the **Redirect URI** to `http://localhost`.
  - Allow the **Implicit grant** flow for **ID tokens**.
  - Create a **client secret** for the web app.
- Configure the app settings with all required values from the steps above:
  - I.e. take the correct values for the app client id and client secret and store them in the `appsettings.json` file or (preferred for local development) in [.NET User Secrets](https://docs.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-3.1&tabs=windows) or (preferred in cloud hosting platforms) through the appropriate app settings.
- Run the application and sign in with an admin user of **any** Azure AD tenant (typically _not_ the one where you registered the app which would be the vendor tenant in this scenario).
