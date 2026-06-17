using System.Text.Json;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;
using Repositories;
using Services;
using WebApiShop.Middleware;
using WebApiShop.Middlewares;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;

namespace WebApiShop
{
    public class Startup
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public Startup(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            var connectionString = _config.GetConnectionString("ShowsCenter");
            // In tests we may replace the DbContext; avoid registering SQL Server when running under the Testing env
            if (!_env.IsEnvironment("Testing"))
            {
                services.AddDbContext<ShowsCenterContext>(options => options.UseSqlServer(connectionString));
            }

            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ICategoryRepository, CategoryRepository>();
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<IProviderRepository, ProviderRepository>();
            services.AddScoped<ISectionRepository, SectionRepository>();
            services.AddScoped<IShowsRepository, ShowsRepository>();
            services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
            services.AddScoped<IRatingRepository, RatingRepository>();

            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                });

            services.AddOpenApi();
            services.AddSwaggerGen(c =>
            {
                var securityScheme = new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter JWT token or click 'Authorize' and paste: Bearer {token}"
                };

                c.AddSecurityDefinition("Bearer", securityScheme);
                var securityRequirement = new OpenApiSecurityRequirement { { securityScheme, new[] { "Bearer" } } };
                c.AddSecurityRequirement(securityRequirement);
            });

            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IAuth, Auth>();
            services.AddScoped<IPasswordService, PasswordService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<IProviderService, ProviderService>();
            services.AddScoped<ISectionService, SectionService>();
            services.AddScoped<IShowService, ShowService>();

            var redisConn = _config["RedisCacheOptions:Configuration"];
            services.AddHybridCache();
            if (!string.IsNullOrEmpty(redisConn))
            {
                services.AddStackExchangeRedisCache(options => { options.Configuration = redisConn; });
            }

            services.Configure<ForgotPasswordServiceOptions>(_config.GetSection("PasswordReset"));
            services.Configure<EmailSenderOptions>(_config.GetSection("Email"));
            services.AddScoped<IEmailSender, EmailSender>();
            services.AddScoped<IForgotPasswordService, ForgotPasswordService>();
            services.AddScoped<IOrderConfirmationEmailService, OrderConfirmationEmailService>();
            services.AddScoped<IRatingService, RatingService>();

            services.AddExceptionHandler<ErrorHandlingMiddleware>();
            services.AddProblemDetails();

            services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.WithOrigins("https://localhost:8080", "http://localhost:44304")
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                });
            });

            var jwtSecret = _config["JwtSettings:SecretKey"] ?? string.Empty;
            var jwtIssuer = _config["JwtSettings:Issuer"] ?? string.Empty;
            var jwtAudience = _config["JwtSettings:Audience"] ?? string.Empty;
            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateIssuer = !string.IsNullOrEmpty(jwtIssuer),
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = !string.IsNullOrEmpty(jwtAudience),
                    ValidAudience = jwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var cookie = context.Request.Cookies["X-Access-Token"];
                        if (!string.IsNullOrEmpty(cookie)) context.Token = cookie;
                        return Task.CompletedTask;
                    }
                };
            });

            services.AddRateLimiter(options =>
            {
                options.AddSlidingWindowLimiter("MySlidingPolicy", opt =>
                {
                    opt.PermitLimit = 10;
                    opt.Window = TimeSpan.FromMinutes(5);
                    opt.SegmentsPerWindow = 3;
                    opt.QueueLimit = 2;
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                });
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseRateLimiter();
            app.UseExceptionHandler();
            app.UseRating();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            if (env.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(options => { options.SwaggerEndpoint("/openapi/v1.json", "My API V1"); });
            }

            app.UseCors();
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }
    }
}
