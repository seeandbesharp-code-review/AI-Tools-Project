using System.Text.Json;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NLog.Web;
using Repositories;
using Services;
using WebApiShop.Middleware;
using WebApiShop.Middlewares;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;



var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("ShowsCenter");
// In normal runs register SQL Server. In tests we want to override the DbContext with an in-memory Sqlite provider,
// avoid registering SQL Server provider when running under the "Testing" environment to prevent multiple providers
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<ShowsCenterContext>(options =>
        options.UseSqlServer(connectionString));
}
// Add services to the container.
//builder.Services.AddScoped<ILoginRepository, LoginRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

builder.Services.AddScoped<IProviderRepository, ProviderRepository>();
builder.Services.AddScoped<ISectionRepository, SectionRepository>();
builder.Services.AddScoped<IShowsRepository, ShowsRepository>();
builder.Services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
builder.Services.AddScoped<IRatingRepository, RatingRepository>();
//builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    }); 
builder.Services.AddOpenApi();
// Configure Swagger/OpenAPI with JWT Bearer security so the "Authorize" button is available in UI
builder.Services.AddSwaggerGen(c =>
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

    var securityRequirement = new OpenApiSecurityRequirement
    {
        { securityScheme, new[] { "Bearer" } }
    };
    c.AddSecurityRequirement(securityRequirement);
});
//builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuth, Auth>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IOrderService, OrderService>();

builder.Services.AddScoped<IProviderService, ProviderService>();
builder.Services.AddScoped<ISectionService, SectionService>();
builder.Services.AddScoped<IShowService, ShowService>();
// Configure HybridCache (uses Redis as a distributed cache backing)
// Ensure Redis:ConnectionString is present in configuration
// 1. שליפת מחרוזת החיבור של רדיס מה-appsettings
var redisConn = builder.Configuration["RedisCacheOptions:Configuration"];

// 2. רישום ה-HybridCache הבסיסי של .NET 9
builder.Services.AddHybridCache();

// 3. חיבור לרדיס באמצעות המתודה הרשמית והיציבה
if (!string.IsNullOrEmpty(redisConn))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConn;
    });
}
builder.Services.Configure<ForgotPasswordServiceOptions>(
    builder.Configuration.GetSection("PasswordReset"));
builder.Services.Configure<EmailSenderOptions>(
    builder.Configuration.GetSection("Email"));
// Bind Kafka settings for producers/consumers
builder.Services.Configure<Services.KafkaSettings>(builder.Configuration.GetSection("Kafka"));
// Register Kafka producer service (interface -> implementation)
builder.Services.AddSingleton<IKafkaProducerService, KafkaProducerService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<IForgotPasswordService, ForgotPasswordService>();
builder.Services.AddScoped<IOrderConfirmationEmailService, OrderConfirmationEmailService>();
builder.Services.AddScoped<IRatingService, RatingService>();

builder.Services.AddExceptionHandler<ErrorHandlingMiddleware>();
builder.Services.AddProblemDetails();

//builder.Services.AddScoped<IProductService, ProductService>();


builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());
builder.Host.UseNLog();
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.WithOrigins("https://localhost:8080", "http://localhost:44304")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // <--- השורה הזו חובה בשביל לאפשר קוקיז!
    });
});

// JWT Authentication
var jwtSecret = builder.Configuration["JwtSettings:SecretKey"] ?? string.Empty;
var jwtIssuer = builder.Configuration["JwtSettings:Issuer"] ?? string.Empty;
var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? string.Empty;

// Provide a fallback secret when running tests so the test host can build a signing key.
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException("Configuration value JwtSettings:SecretKey is missing. Set it in appsettings or environment variables.");
}
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddAuthentication(options =>
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
            // Try to get token from X-Access-Token cookie
            var cookie = context.Request.Cookies["X-Access-Token"];
            if (!string.IsNullOrEmpty(cookie))
            {
                context.Token = cookie;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddRateLimiter(options =>
{
    // הגדרת מדיניות (Policy) מסוג Sliding Window
    options.AddSlidingWindowLimiter("MySlidingPolicy", opt =>
    {
        opt.PermitLimit = 10; // מקסימום 10 בקשות
        opt.Window = TimeSpan.FromMinutes(5); // בתוך חלון זמן של דקה אחת
        opt.SegmentsPerWindow = 3; // חלוקת הדקה ל-3 מקטעים (כלומר, בדיקה דינמית כל 20 שניות)
        opt.QueueLimit = 2; // כמה בקשות יכולות להמתין בתור אם עברנו את המכסה (0 אומר לחסום מיד)
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    // מה קורה כשמשתמש נחסם? הגדרת קוד שגיאה 429
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

app.UseRateLimiter();
app.UseExceptionHandler();
app.UseRating();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "My API V1");
    });
}
app.UseCors();

// ...

app.UseHttpsRedirection();

app.UseStaticFiles();

app.MapControllers();

// When running under the integration test host we avoid calling Run() to let the test
// infrastructure manage the host lifecycle. Tests set the environment to "Testing".
if (!app.Environment.IsEnvironment("Testing"))
{
    app.Run();
}
