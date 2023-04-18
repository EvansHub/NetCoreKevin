﻿using Kevin.Common.TieredServiceRegistration;
using Kevin.Cors;
using Kevin.Cors.Models;
using Kevin.SignalR;
using Kevin.SignalR.Models;
using Kevin.Web.Extensions;
using Medallion.Threading;
using Medallion.Threading.SqlServer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Models.Extension;
using Repository.Database;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Web.Filters;
using Web.Global;
using Web.Global.User;
using Web.Libraries.Start;
using Web.Libraries.Swagger;
using Web.Permission.Action;

namespace Web.Extension
{
    public static class ServiceConfiguration
    {
        public static IServiceCollection ConfigServies(this IServiceCollection services, IConfiguration Configuration)
        {
            #region json配置
            //json动态响应压缩https://docs.microsoft.com/zh-cn/aspnet/core/performance/response-compression?view=aspnetcore-5.0
            services.AddResponseCompression();
            services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = long.MaxValue;
            });

            services.Configure<KestrelServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });

            services.Configure<IISServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });

            services.AddHsts(options =>
            {
                options.MaxAge = TimeSpan.FromDays(365);
            });
            #endregion

            #region 权限校验

            //权限校验
            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireAssertion(context => IdentityVerification.Authorization(context)).Build();
            });
            #endregion

            #region 注册常用 

            //注册全局过滤器
            services.AddMvc(config =>
            {
                config.Filters.Add(new ResultFilter());
            });
            //注册配置文件信息
            Web.Libraries.Start.StartConfiguration.Add(Configuration);

            //注册HttpContext
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            //注册雪花ID算法示例
            services.AddSingleton(new Common.SnowflakeHelper(0, 0));
            #endregion

            services.AddControllers();

            services.AddControllers().AddJsonOptions(option =>
            {
                option.JsonSerializerOptions.Converters.Add(new Common.Json.DateTimeConverter());
                option.JsonSerializerOptions.Converters.Add(new Common.Json.DateTimeNullConverter());
                option.JsonSerializerOptions.Converters.Add(new Common.Json.LongConverter());
            });

            services.AddControllers().AddControllersAsServices(); //控制器当做实例创建


            #region Api版本以及配置
            services.AddApiVersioning(options =>
            {
                //通过Header向客户端通报支持的版本
                options.ReportApiVersions = true;

                //允许不加版本标记直接调用接口
                options.AssumeDefaultVersionWhenUnspecified = true;

                //接口默认版本
                //options.DefaultApiVersion = new ApiVersion(1, 0);

                //如果未加版本标记默认以当前最高版本进行处理
                options.ApiVersionSelector = new CurrentImplementationApiVersionSelector(options);

                options.ApiVersionReader = new HeaderApiVersionReader("api-version");
                //options.ApiVersionReader = new QueryStringApiVersionReader("api-version");
            });


            services.AddVersionedApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

            //注册统一模型验证
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = actionContext =>
                {

                    //获取验证失败的模型字段 
                    var errors = actionContext.ModelState.Where(e => e.Value.Errors.Count > 0).Select(e => e.Value.Errors.First().ErrorMessage).ToList();

                    var dataStr = string.Join(" | ", errors);

                    //设置返回内容
                    var result = new
                    {
                        errMsg = dataStr
                    };

                    return new BadRequestObjectResult(result);
                };
            });
            services.AddTransient<IConfigureOptions<SwaggerGenOptions>, SwaggerConfigureOptions>();
            #endregion


            #region 缓存服务模式
            //注册缓存服务 内存模式
            services.AddDistributedMemoryCache();


            //注册缓存服务 SqlServer模式
            //services.AddDistributedSqlServerCache(options =>
            //{
            //    options.ConnectionString = Configuration.GetConnectionString("dbConnection");
            //    options.SchemaName = "dbo";
            //    options.TableName = "t_cache";
            //});


            //注册缓存服务 Redis模式
            //services.AddStackExchangeRedisCache(options =>
            //{
            //    options.Configuration = Configuration.GetConnectionString("redisConnection");
            //    options.InstanceName = "cache";
            //});
            #endregion

            #region 分布式锁服务注册
            //分布式
            services.AddSingleton<IDistributedLockProvider>(new SqlDistributedSynchronizationProvider(Configuration.GetConnectionString("dbConnection")));
            //信号锁
            services.AddSingleton<IDistributedSemaphoreProvider>(new SqlDistributedSynchronizationProvider(Configuration.GetConnectionString("dbConnection")));
            //读写锁
            services.AddSingleton<IDistributedUpgradeableReaderWriterLockProvider>(new SqlDistributedSynchronizationProvider(Configuration.GetConnectionString("dbConnection")));
            #endregion

            //把控制器作为服务注册，然后使用它内置的ioc来替换原来的控制器的创建器
            services.AddControllersWithViews().AddControllersAsServices();
            services.Replace(ServiceDescriptor.Transient<IControllerActivator, IocServiceBaseControllerActivator>());
            //App服务注册
            RegisterAppServices(services, Configuration);
            services.AddKevinCors(JsonExtensions.DeserializeFromJson<CorsSetting>(Configuration.GetSection("CorsSetting").SerializeToJson()));
            services.AddKevinSignalR(Configuration);
            services.RunModuleInitializers(ReflectionScheduler.GetAllReferencedAssemblies());//初始化
            return services;
        }

        /// <summary>
        /// 使用UseKevin
        /// UseKevin
        /// </summary>
        /// <param name="app"></param>
        /// <returns></returns>
        public static IApplicationBuilder UseKevin(this IApplicationBuilder app)
        {
            /////json压缩
            app.UseResponseCompression();
            app.UseHsts();
            //注册跨域信息
            app.UseKevinCors();
            //强制重定向到Https
            app.UseHttpsRedirection();

            //静态文件中间件 (UseStaticFiles) 返回静态文件，并简化进一步请求处理。
            //app.UseStaticFiles();

            app.UseRouting();

            //注册用户认证机制,必须放在 UseCors UseRouting 之后
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            app.UseKevinSignalR(new SignalrSetting { url = StartConfiguration.configuration["SignalrSetting:url"] });
            //启用中间件服务生成Swagger作为JSON端点
            app.UseSwagger();
            GlobalServices.Set(app.ApplicationServices);
            return app;
        }

        /// <summary>
        /// App服务注册
        /// </summary>
        /// <param name="services"></param>
        /// <param name="assembly"></param>
        /// <returns></returns>
        public static void RegisterAppServices(this IServiceCollection services, IConfiguration Configuration)
        {
            //AddSingleton→AddTransient→AddScoped

            //AddSingleton的生命周期：

            //项目启动 - 项目关闭   相当于静态类 只会有一个

            //AddScoped的生命周期：

            //请求开始 - 请求结束  在这次请求中获取的对象都是同一个

            //  AddTransient的生命周期：

            //请求获取 -（GC回收 - 主动释放） 每一次获取的对象都不是同一个
            #region 基本服务
            //为各数据库注入连接字符串
            Repository.Database.dbContext.ConnectionString = Configuration.GetConnectionString("dbConnection");
            services.AddDbContextPool<Repository.Database.dbContext>(options => { }, 100);
            services.AddScoped<dbContext, dbContext>();
            services.AddScoped<ICurrentUser, CurrentUser>();
            #endregion

        }
    }
}
