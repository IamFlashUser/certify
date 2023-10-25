﻿using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;
using System.Web.Http.Results;
using LightInject;
using Microsoft.AspNet.SignalR;
using Newtonsoft.Json.Serialization;
using Owin;
using Swashbuckle.Application;

namespace Certify.Service
{
    public class CustomExceptionHandler : IExceptionHandler
    {
        public Task HandleAsync(ExceptionHandlerContext context, CancellationToken cancellationToken)
        {
            context.Result = new ResponseMessageResult(
                context.Request.CreateResponse(HttpStatusCode.InternalServerError,
                new
                {
                    Message = $"An internal error has occurred. If this problem happens regularly please report it to support@certifytheweb.com: {context.Exception?.Message} {context.Exception.Source} {context.Exception.StackTrace}",
                    Exception = context.Exception
                },
                JsonMediaTypeFormatter.DefaultMediaType
            ));

            return Task.FromResult(0);
        }
    }

    public partial class APIHost
    {
        private System.Timers.Timer _frequentTimer;
        private System.Timers.Timer _hourlyTimer;
        private System.Timers.Timer _dailyTimer;

        private ServiceContainer _container;

        public void Configuration(IAppBuilder appBuilder)
        {

            var config = new HttpConfiguration();

            config.Services.Replace(typeof(IExceptionHandler), new CustomExceptionHandler());

            // enable windows auth credentials

            var owinHttp = appBuilder.Properties["Microsoft.Owin.Host.HttpListener.OwinHttpListener"] as Microsoft.Owin.Host.HttpListener.OwinHttpListener;
            owinHttp.Listener.AuthenticationSchemes = AuthenticationSchemes.IntegratedWindowsAuthentication | AuthenticationSchemes.Anonymous;
            owinHttp.Listener.AuthenticationSchemeSelectorDelegate = IdentifyAuthentication;

#if DEBUG
            config
              .EnableSwagger(c => c.SingleApiVersion("v1", "Service API for local install of Certify Certificate Manager"))
              .EnableSwaggerUi();
#endif

            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
                );

            config.Formatters.JsonFormatter.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            appBuilder.UseCors(Microsoft.Owin.Cors.CorsOptions.AllowAll);

            appBuilder.MapSignalR("/api/status", new HubConfiguration());
            appBuilder.UseWebApi(config);

            _container = new ServiceContainer();

            /// restrict type we will load controller from to only our service assembly
            _container.RegisterApiControllers(this.GetType().Assembly);

            _container.EnableWebApi(config);

            // inject single CertifyManager for service to use
            _container.Register<Management.ICertifyManager, Management.CertifyManager>(new PerContainerLifetime());

            var currentCertifyManager = _container.GetInstance<Management.ICertifyManager>();
            currentCertifyManager.Init().Wait();

            // attached handlers for SignalR hub updates
            currentCertifyManager.SetStatusReporting(new StatusHubReporting());

            // 5 minute job timer (maintenance etc)
            _frequentTimer = new System.Timers.Timer(5 * 60 * 1000); // every 5 minutes
            _frequentTimer.Elapsed += _frequentTimer_Elapsed;
            _frequentTimer.Start();

            // hourly jobs timer (renewal etc)
            _hourlyTimer = new System.Timers.Timer(60 * 60 * 1000); // every 60 minutes
            _hourlyTimer.Elapsed += _hourlyTimer_Elapsed;
            _hourlyTimer.Start();

            // daily jobs timer (cleanup etc)
            _dailyTimer = new System.Timers.Timer(24 * 60 * 60 * 1000); // every 24 hrs
            _dailyTimer.Elapsed += _dailyTimer_Elapsed;
            _dailyTimer.Start();
        }

        private AuthenticationSchemes IdentifyAuthentication(HttpListenerRequest request)
        {
            if (request.HttpMethod == "OPTIONS")
            {
                return AuthenticationSchemes.Anonymous;
            }

#if DEBUG   // feature not in production yet
            // allow JWT pass through if provided
            if (request.Headers["Authorization"] != null && request.Headers["Authorization"].Contains("Bearer "))
            {
                return AuthenticationSchemes.Anonymous;
            }

            // hack to allow anonymous auth on /api/auth/token without an Authorization: Bearer header
            if (request.RawUrl.EndsWith("/auth/token"))
            {
                return AuthenticationSchemes.Anonymous;
            }
#endif

            // for windows auth require windows credentials
            return AuthenticationSchemes.IntegratedWindowsAuthentication;
        }
        private async void _dailyTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            var currentCertifyManager = _container.GetInstance<Management.ICertifyManager>();
            if (currentCertifyManager != null)
            {
                await currentCertifyManager.PerformDailyTasks();
            }
        }

        private async void _hourlyTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            var currentCertifyManager = _container.GetInstance<Management.ICertifyManager>();
            if (currentCertifyManager != null)
            {
                await currentCertifyManager.PerformCertificateMaintenance();
            }
        }

        private async void _frequentTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            var currentCertifyManager = _container.GetInstance<Management.ICertifyManager>();
            if (currentCertifyManager != null)
            {
                await currentCertifyManager.PerformRenewalTasks();
            }
        }
    }
}
