using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.AzureAD.UI;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DeviceIdentityProvisioning.WebApp
{
    public class Startup
    {
        public const string ClaimTypeAccessToken = "access_token";
        public const string ClaimTypeRefreshToken = "refresh_token";

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Configure support for the SameSite cookies breaking change.
            services.ConfigureSameSiteCookiePolicy();

            // Add Azure AD authentication using OpenID Connect.
            services.AddAuthentication(AzureADDefaults.AuthenticationScheme)
                .AddAzureAD(options => Configuration.Bind("AzureAd", options));
            services.Configure<OpenIdConnectOptions>(AzureADDefaults.OpenIdScheme, options =>
            {
                // Use the Azure AD v2.0 endpoint.
                options.Authority += "/v2.0";

                // Trigger a hybrid OIDC + auth code flow.
                options.ResponseType = OpenIdConnectResponseType.CodeIdToken;

                // Request a refresh token as part of the auth code flow.
                options.Scope.Add(OpenIdConnectScope.OfflineAccess);

                // The following delegated permissions are required to allow the customer to register applications in their tenant.
                // Note that these require admin consent, so the end user must be an admin in their tenant.
                options.Scope.Add("https://graph.microsoft.com/Application.ReadWrite.All");
                options.Scope.Add("https://graph.microsoft.com/Directory.AccessAsUser.All");

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // The Azure AD v2.0 endpoint returns the display name in the "preferred_username" claim for ID tokens.
                    NameClaimType = "preferred_username",

                    // Instead of using the default validation (validating against a single issuer value, as we do in
                    // line of business apps), we inject our own multitenant validation logic
                    ValidateIssuer = false,

                    // If the app is meant to be accessed by entire organizations, add your issuer validation logic here.
                    //IssuerValidator = (issuer, securityToken, validationParameters) => {
                    //    if (myIssuerValidationLogic(issuer)) return issuer;
                    //}
                };

                options.Events = new OpenIdConnectEvents
                {
                    OnTicketReceived = context =>
                    {
                        // If your authentication logic is based on users then add your logic here
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        context.Response.Redirect("/Error");
                        context.HandleResponse(); // Suppress the exception
                        return Task.CompletedTask;
                    },
                    OnTokenResponseReceived = context =>
                    {
                        // Normally, the access and refresh tokens that resulted from the authorization code flow would be
                        // stored in a cache like MSAL's user cache.
                        // To simplify here, we're adding them as extra claims in the user's claims identity
                        // (which is ultimately encrypted and serialized into the authentication cookie).
                        var identity = (ClaimsIdentity)context.Principal.Identity;
                        identity.AddClaim(new Claim(ClaimTypeAccessToken, context.TokenEndpointResponse.AccessToken));
                        identity.AddClaim(new Claim(ClaimTypeRefreshToken, context.TokenEndpointResponse.RefreshToken));
                        return Task.CompletedTask;
                    }
                    // If your application needs to do authenticate single users, add your user validation below.
                    //OnTokenValidated = context =>
                    //{
                    //    return myUserValidationLogic(context.Ticket.Principal);
                    //}
                };
            });

            services.AddRazorPages();
            services.AddRouting(options => { options.LowercaseUrls = true; });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
            });
        }
    }
}
