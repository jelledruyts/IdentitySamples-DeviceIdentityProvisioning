using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace DeviceIdentityProvisioning.WebApp
{
    [Authorize]
    public class ConsentModel : PageModel
    {
        private readonly ILogger<ConsentModel> logger;
        public string NotificationMessage { get; set; }

        public ConsentModel(ILogger<ConsentModel> logger)
        {
            this.logger = logger;
        }

        public void OnGet(string notificationMessage)
        {
            this.NotificationMessage = notificationMessage;
        }

        public async Task<IActionResult> OnPost()
        {
            // Revoke consent for the entire organization by removing the Service Principal of the application in the end user's tenant.
            var graphService = GraphServiceClientFactory.GetForUserIdentity(this.User);
            var deviceIdentityProvisioningAppId = Startup.DeviceIdentityProvisioningAppId;
            var deviceIdentityProvisioningServicePrincipal = (await graphService.ServicePrincipals.Request().Filter($"appId eq '{deviceIdentityProvisioningAppId}'").GetAsync()).SingleOrDefault();
            var notificationMessage = default(string);
            if (deviceIdentityProvisioningServicePrincipal != null)
            {
                await graphService.ServicePrincipals[deviceIdentityProvisioningServicePrincipal.Id].Request().DeleteAsync();
                notificationMessage = "Consent was revoked successfully. Please sign out.";
            }
            else
            {
                notificationMessage = "Consent was already revoked. Please sign out.";
            }
            return RedirectToPage(new { notificationMessage = notificationMessage });
        }
    }
}