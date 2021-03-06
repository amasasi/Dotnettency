﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Dotnettency;
using System;
using System.Text;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Dotnettency.Modules;
using Microsoft.AspNetCore.Routing;
using Dotnettency.Container.StructureMap;
using Dotnettency.Container;

namespace Sample
{
    public class Startup
    {
        private readonly IHostingEnvironment _environment;
        private readonly ILoggerFactory _loggerFactory;

        public Startup(IHostingEnvironment environment, ILoggerFactory loggerFactory)
        {
            _environment = environment;
            _loggerFactory = loggerFactory;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            _loggerFactory.AddConsole();
            var logger = _loggerFactory.CreateLogger<Startup>();

            var serviceProvider = services.AddMultiTenancy<Tenant>((options) =>
            {
                options
                    .InitialiseTenant<TenantShellFactory>() // factory class to load tenant when it needs to be initialised for the first time. Can use overload to provide a delegate instead.                    
                    .ConfigureTenantContainers((containerBuilder) =>
                   {
                       // Extension methods available here for supported containers. We are using structuremap..
                       // We are using an overload that allows us to configure structuremap with familiar IServiceCollection.
                       containerBuilder.WithStructureMap((tenant, tenantServices) =>
                       {
                           tenantServices.AddSingleton<SomeTenantService>((sp) =>
                           {
                               //var logger = sp.GetRequiredService<ILogger<SomeTenantService>>();
                               logger.LogDebug("Resolving SomeTenantService");
                               return new SomeTenantService(tenant, sp.GetRequiredService<IHostingEnvironment>());
                           });

                           var moMatchRouteHandler = new RouteHandler(context =>
                           {
                               // default route handler when a single module router fails to match.
                               // Return null so it can flow through to the next module
                               return null;
                           });

                           tenantServices.AddModules<ModuleBase>((modules) =>
                           {
                               // Only load these two modules for tenant Bar.
                               if (tenant?.Name == "Bar")
                               {
                                   modules.AddModule<SampleRoutedModule>()
                                          .AddModule<SampleSharedModule>();
                               }

                               // Configure each modules shell. This allows the module to participate in:
                               // 1. Configuring tenant / application level services
                               // 2. Optionally providing an IRouter so that the services can be isolated and restored only when the router handles the request.
                               modules.OnSetupModule((moduleOptions) =>
                               {
                                   logger.LogDebug("Setting up module: " + moduleOptions.Module.GetType().FullName);
                                   // we allow ISharedModules to configure services at tenant level, and middleware at tenant level.
                                   var sharedModule = moduleOptions.Module as ISharedModule;
                                   if (sharedModule != null)
                                   {
                                       moduleOptions.HasSharedServices((moduleServices) =>
                                       {
                                           logger.LogDebug("Module is adding to tenant services");
                                           sharedModule.ConfigureServices(moduleServices);
                                       });
                                       moduleOptions.HasMiddlewareConfiguration((appBuilder) =>
                                       {
                                           logger.LogDebug("Module is adding to tenant middleware pipeline");
                                           sharedModule.ConfigureMiddleware(appBuilder);
                                       });
                                   }

                                   // We allow IRoutedModules to partipate in configuring their own isolated services, associated with their router 
                                   var routedModule = moduleOptions.Module as IRoutedModule;
                                   if (routedModule != null)
                                   {
                                       logger.LogDebug("Module is confoguring router");
                                       moduleOptions.HasRoutedContainer((moduleAppBuilder) =>
                                       {
                                           var moduleRouteBuilder = new RouteBuilder(moduleAppBuilder, moMatchRouteHandler);
                                           routedModule.ConfigureRoutes(moduleRouteBuilder);
                                           var moduleRouter = moduleRouteBuilder.Build();
                                           return moduleRouter;
                                       },
                                       moduleServices => routedModule.ConfigureServices(moduleServices));
                                   }

                               });
                           });
                       });

                       // .WithModuleContainers(); // Creates a child container per IModule.
                   })
                    .ConfigureTenantMiddleware((middlewareOptions) =>
                    {

                        // This method is called when need to initialise the middleware pipeline for a tenant (i.e on first request for the tenant)
                        middlewareOptions.OnInitialiseTenantPipeline((context, appBuilder) =>
                        {
                            logger.LogDebug("Configuring tenant middleware pipeline for tenant: " + context.Tenant?.Name);
                            // appBuilder.UseStaticFiles(); // This demonstrates static files middleware, but below I am also using per tenant hosting environment which means each tenant can see its own static files in addition to the main application level static files.

                            appBuilder.UseModules<Tenant, ModuleBase>();

                            if (context.Tenant?.Name == "Foo")
                            {
                                appBuilder.UseWelcomePage("/welcome");
                            }
                            //
                        });
                    }) // Configure per tenant containers.

                // configure per tenant hosting environment.
                .ConfigurePerTenantHostingEnvironment(_environment, (tenantHostingEnvironmentOptions) =>
                {
                    tenantHostingEnvironmentOptions.OnInitialiseTenantContentRoot((contentRootOptions) =>
                    {
                        // WE use a tenant's guid id to partition one tenants files from another on disk.
                        // NOTE: We use an empty guid for NULL tenants, so that all NULL tenants share the same location.
                        var tenantGuid = (contentRootOptions.Tenant?.TenantGuid).GetValueOrDefault();
                        contentRootOptions.TenantPartitionId(tenantGuid)
                                           .AllowAccessTo(_environment.ContentRootFileProvider); // We allow the tenant content root file provider to access to the environments content root.
                    });

                    tenantHostingEnvironmentOptions.OnInitialiseTenantWebRoot((webRootOptions) =>
                    {
                        // WE use the tenant's guid id to partition one tenants files from another on disk.
                        var tenantGuid = (webRootOptions.Tenant?.TenantGuid).GetValueOrDefault();
                        webRootOptions.TenantPartitionId(tenantGuid)
                                           .AllowAccessTo(_environment.WebRootFileProvider); // We allow the tenant web root file provider to access the environments web root files.
                    });
                });
            });

            // When using tenant containers, must return IServiceProvider.
            return serviceProvider;
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            //  loggerFactory.AddConsole();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Add the multitenancy middleware.
            app.UseMultitenancy<Tenant>((options) =>
            {
                options
                       .UsePerTenantContainers()
                       .UsePerTenantHostingEnvironment((hostingEnvironmentOptions) =>
                        {
                            // using tenant content root and web root.
                            hostingEnvironmentOptions.UseTenantContentRootFileProvider();
                            hostingEnvironmentOptions.UseTenantWebRootFileProvider();
                        })
                       .UsePerTenantMiddlewarePipeline();
                //.UseModules<Tenant, ModuleBase>();

            });

            //app.UseOwin(x =>
            //{
            //    x.UseMyMiddleware(new MyMiddlewareOptions());
            //    x.UseNancy();
            //});


            //  app.UseMiddleware<SampleMiddleware<Tenant>>();
            //  app.
            app.Run(async (context) =>
            {
                var logger = context.RequestServices.GetRequiredService<ILogger<Startup>>();
                logger.LogDebug("App Run..");

                var container = context.RequestServices as ITenantContainerAdaptor;
                logger.LogDebug("App Run Container Is: {id}, {containerNAme}, {role}", container.ContainerId, container.ContainerName, container.Role);


                // Use ITenantAccessor to access the current tenant.
                var tenantAccessor = container.GetRequiredService<ITenantAccessor<Tenant>>();
                var tenant = await tenantAccessor.CurrentTenant.Value;

                // This service was registered as singleton in tenant container.
                var someTenantService = container.GetService<SomeTenantService>();

                // The tenant shell to access context for the tenant - even if the tenant is null
                var tenantShellAccessor = context.RequestServices.GetRequiredService<ITenantShellAccessor<Tenant>>();
                var tenantShell = await tenantShellAccessor.CurrentTenantShell.Value;


                string tenantShellId = tenantShell == null ? "{NULL TENANT SHELL}" : tenantShell.Id.ToString();
                string tenantName = tenant == null ? "{NULL TENANT}" : tenant.Name;
                string injectedTenantName = someTenantService?.TenantName == null ? "{NULL SERVICE}" : someTenantService?.TenantName;

                // Accessing a content file.
                string fileContent = someTenantService?.GetContentFile("/Info.txt");
                context.Response.ContentType = new MediaTypeHeaderValue("application/json").ToString();
                var result = new
                {
                    TenantShellId = tenantShellId,
                    TenantName = tenantName,
                    TenantScopedServiceId = someTenantService?.Id,
                    InjectedTenantName = injectedTenantName,
                    TenantContentFile = fileContent
                };

                var jsonResult = JsonConvert.SerializeObject(result);
                await context.Response.WriteAsync(jsonResult, Encoding.UTF8);
                logger.LogDebug("App Run Finished..");

                //    context.Response.

                // for null tenants we could optionally redirect somewhere?
            });
        }
    }
}
