using System;
using Microsoft.Graph;

namespace DeviceIdentityProvisioning.WebApp
{
    public class DeviceIdentity
    {
        public string DisplayName { get; set; }
        public string Id { get; set; }
        public string AppId { get; set; }
        public string TenantId { get; set; }
        public string ClientSecret { get; set; }
        public DateTimeOffset? CreatedDateTime { get; set; }

        public DeviceIdentity()
        {
        }

        public DeviceIdentity(Application application)
        {
            this.DisplayName = application.DisplayName;
            this.Id = application.Id;
            this.AppId = application.AppId;
            this.CreatedDateTime = application.CreatedDateTime;

            // For this sample app, the Notes field stores the necessary other information we need to be able to perform an API call.
            // In reality, this information would NEVER be stored inside the application object itself of course.
            foreach (var additionalField in application.Notes.Split(';'))
            {
                var components = additionalField.Split('=');
                var key = components[0];
                var value = components[1];
                if (key == nameof(TenantId))
                {
                    this.TenantId = value;
                }
                else if (key == nameof(ClientSecret))
                {
                    this.ClientSecret = value;
                }
            }
        }
    }
}