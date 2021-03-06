﻿using IoTSharp.Data;
using IoTSharp.Extensions;
using IoTSharp.Handlers;
using IoTSharp.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MQTTnet.AspNetCore;
using MQTTnet.AspNetCoreEx;
using MQTTnet.Client;
using NSwag.AspNetCore;
using Quartz;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MQTTnet.Server;
using System.Runtime.InteropServices.ComTypes;
using SshNet.Security.Cryptography;
using SilkierQuartz;
using HealthChecks.UI.Client;
using NSwag;
using NSwag.Generation.Processors.Security;
using IoTSharp.Queue;
using Npgsql;
using EFCore.Sharding;
using IoTSharp.Storage;
using DotNetCore.CAP.Dashboard.NodeDiscovery;
using Savorboard.CAP.InMemoryMessageQueue;

namespace IoTSharp
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            settings = Configuration.Get<AppSettings>();
            if (settings.MqttBroker == null) settings.MqttBroker = new MqttBrokerSetting();
            if (settings.MqttClient == null) settings.MqttClient = new MqttClientSetting();
            if (string.IsNullOrEmpty(settings.MqttClient.MqttBroker)) settings.MqttClient.MqttBroker = "built-in";
            if (string.IsNullOrEmpty(settings.MqttClient.Password)) settings.MqttClient.Password = Guid.NewGuid().ToString();
            if (string.IsNullOrEmpty(settings.MqttClient.UserName)) settings.MqttClient.UserName = Guid.NewGuid().ToString();
            if (settings.MqttClient.Port == 0) settings.MqttClient.Port = 1883;
        }
        private AppSettings settings;
        public IConfiguration Configuration { get; private set; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure((Action<AppSettings>)(setting =>
            {
                Configuration.Bind(setting);
                setting.MqttBroker = settings.MqttBroker;
                setting.MqttClient = settings.MqttClient;
            }));
            services.AddDbContext<ApplicationDbContext>(options =>  options.UseNpgsql(Configuration.GetConnectionString("IoTSharp"))      
            , ServiceLifetime.Transient);
            services.AddIdentity<IdentityUser, IdentityRole>()
                  .AddRoles<IdentityRole>()
                  .AddRoleManager<RoleManager<IdentityRole>>()
                 .AddDefaultTokenProviders()
                  .AddEntityFrameworkStores<ApplicationDbContext>();

            services.AddControllersWithViews();
       
            services.AddCors();

            services.AddAuthentication(option =>
            {
                option.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                option.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

            }).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = false,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = Configuration["JwtIssuer"],
                    ValidAudience = Configuration["JwtAudience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["JwtKey"]))
                };
            });
     
       
          
            services.AddLogging(loggingBuilder => loggingBuilder.AddConsole());
          
            // Enable the Gzip compression especially for Kestrel
            services.Configure<GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Optimal);
            services.AddResponseCompression(options =>
                {
                    options.EnableForHttps = true;
                });



            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            });
            services.AddOpenApiDocument(configure =>
            {
                Assembly assembly = typeof(Startup).GetTypeInfo().Assembly;
                var description = (AssemblyDescriptionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyDescriptionAttribute));
                configure.Title = typeof(Startup).GetTypeInfo().Assembly.GetName().Name;
                configure.Version = typeof(Startup).GetTypeInfo().Assembly.GetName().Version.ToString();
                configure.Description = description?.Description;
                configure.AddSecurity("JWT", Enumerable.Empty<string>(), new OpenApiSecurityScheme
                {
                    Type = OpenApiSecuritySchemeType.ApiKey,
                    Name = "Authorization",
                    In = OpenApiSecurityApiKeyLocation.Header,
                    Description = "Type into the textbox: Bearer {your JWT token}." 
                });

                configure.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor("JWT"));
            });
            services.AddTransient<ApplicationDBInitializer>();
            services.AddIoTSharpMqttServer(settings.MqttBroker);
            services.AddMqttClient(settings.MqttClient);
            services.AddSingleton<RetainedMessageHandler>();
            services.AddHealthChecks()
                 .AddNpgSql(Configuration["IoTSharp"], name: "PostgreSQL")
                 .AddDiskStorageHealthCheck(dso =>
                 {
                     System.IO.DriveInfo.GetDrives().Select(f=>f.Name).Distinct().ToList().ForEach(f => dso.AddDrive(f, 1024));

                 }, name: "Disk Storage");
            services.AddHealthChecksUI().AddPostgreSqlStorage(Configuration.GetConnectionString("IoTSharp"));
            services.AddSilkierQuartz();
            services.AddMemoryCache();
            switch (settings.TelemetryStorage)
            {
                case TelemetryStorage.Sharding:
                    services.AddEFCoreSharding(config =>
                    {
                        config.AddDataSource(Configuration.GetConnectionString("TelemetryStorage"), ReadWriteType.Read | ReadWriteType.Write, settings.Sharding.DatabaseType)
                        .SetDateSharding<TelemetryData>(nameof(TelemetryData.DateTime), settings.Sharding.ExpandByDateMode, DateTime.MinValue);
                    });
                    services.AddSingleton<IStorage, ShardingStorage>();
                    break;
                case TelemetryStorage.SingleTable:
                    services.AddSingleton<IStorage, EFStorage>();
                    break;
                case TelemetryStorage.Taos:
                    services.AddSingleton<IStorage, TaosStorage>();
                    break;
                default:
                    break;
            }
            //Note: The injection of services needs before of `services.AddCap()`
            services.AddTransient<IEventBusHandler,  EventBusHandler>();
            services.AddCap(x =>
            {
               
                switch (settings.EventBusStore)
                {
                    case EventBusStore.PostgreSql:
                        x.UsePostgreSql(Configuration.GetConnectionString("EventBusStore"));
                        break;
                    case EventBusStore.MongoDB:
                        x.UseMongoDB(Configuration.GetConnectionString("EventBusStore"));  //注意，仅支持MongoDB 4.0+集群
                        break;
                    case EventBusStore.InMemory:
                        x.UseInMemoryStorage();
                        break;
                    default:
                        break;
                }
                switch (settings.EventBusMQ)
                {
                  
                    case EventBusMQ.RabbitMQ:
                        x.UseRabbitMQ(Configuration.GetConnectionString("EventBusMQ"));
                        break;
                    case EventBusMQ.Kafka:
                        x.UseKafka( Configuration.GetConnectionString("EventBusMQ"));
                        break;
                    case EventBusMQ.InMemory:
                        x.UseInMemoryMessageQueue();
                        break;
                    default:
                        break;
                }
                // Register Dashboard
                x.UseDashboard();
                // Register to Consul
                x.UseDiscovery();
            });
          
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ISchedulerFactory factory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseCors(option => option
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseSilkierQuartz(new  SilkierQuartzOptions()
            {
                Scheduler = factory.GetScheduler().Result,
                VirtualPathRoot = "/quartzmin",
                ProductName = "IoTSharp",
                DefaultDateFormat = "yyyy-MM-dd",
                DefaultTimeFormat = "HH:mm:ss",
                UseLocalTime = true
            });
            app.UseIotSharpMqttServer();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            //   endpoints.MapMqtt("/mqtt");
            });
            app.UseSwaggerUi3();
            app.UseOpenApi();
       
    

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });
            app.UseResponseCompression(); // No need if you use IIS, but really something good for Kestrel!
           
            
            app.UseHealthChecks("/healthz", new HealthCheckOptions
            {
                Predicate = _ => true,
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });
            app.UseHealthChecksUI();
        }
    }
}