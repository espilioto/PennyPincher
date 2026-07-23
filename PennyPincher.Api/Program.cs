using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NLog;
using NLog.Web;
using PennyPincher.Data;
using PennyPincher.Services;
using PennyPincher.Services.Accounts;
using PennyPincher.Services.Categories;
using PennyPincher.Services.Charts;
using PennyPincher.Services.EnableBanking;
using PennyPincher.Services.Statements;
using PennyPincher.Services.Users;
using PennyPincher.Services.Utils;
using System.Text;

namespace PennyPincher.Api
{
    public class Program
    {
        public async static Task Main(string[] args)
        {
            var logger = LogManager.Setup().LoadConfigurationFromFile().GetCurrentClassLogger();
            logger.Info("init main");

            try
            {
                var builder = WebApplication.CreateBuilder(args);

                builder.Host.UseNLog();

                // Add services to the container.
                builder.Services.AddControllers();

                builder.Services.AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PennyPincher API", Version = "v1" });

                    // Add JWT Bearer
                    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                    {
                        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                        Name = "Authorization",
                        In = ParameterLocation.Header,
                        Type = SecuritySchemeType.Http,
                        Scheme = "Bearer"
                    });

                    c.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.SecurityScheme,
                                    Id = "Bearer"
                                }
                            },
                            Array.Empty<string>()
                        }
                    });
                });
                builder.Services.AddHealthChecks();

                // entity framework
                var cs = builder.Configuration.GetConnectionString("MariaDbConnectionString");
                builder.Services.AddDbContext<PennyPincherApiDbContext>(o => o.UseMySql(cs, ServerVersion.AutoDetect(cs)));
                builder.Services.AddDatabaseDeveloperPageExceptionFilter();

                builder.Services.AddIdentityCore<IdentityUser>(o =>
                {
                    o.User.RequireUniqueEmail = true;
                    o.Password.RequireDigit = false;
                    o.Password.RequireNonAlphanumeric = false;
                    o.Password.RequiredLength = 0;
                    o.Password.RequireLowercase = false;
                    o.Password.RequireUppercase = false;
                    o.Password.RequiredUniqueChars = 0;
                }).AddEntityFrameworkStores<PennyPincherApiDbContext>();

                builder.Services.AddScoped<IStatementsService, StatementsService>();
                builder.Services.AddScoped<ICategoriesService, CategoriesService>();
                builder.Services.AddScoped<IAccountService, AccountService>();
                builder.Services.AddScoped<IChartDataService, ChartDataService>();
                builder.Services.AddScoped<IUserService, UserService>();
                builder.Services.AddScoped<IUtils, Utils>();

                builder.Services.AddMemoryCache();
                builder.Services.Configure<EnableBankingOptions>(builder.Configuration.GetSection(EnableBankingOptions.SectionName));
                builder.Services.AddSingleton<IEnableBankingJwtFactory, EnableBankingJwtFactory>();
                builder.Services.AddScoped<IEnableBankingClient, EnableBankingClient>();
                builder.Services.AddScoped<IEnableBankingService, EnableBankingService>();
                builder.Services.AddHttpClient(EnableBankingClient.HttpClientName, (sp, client) =>
                {
                    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EnableBankingOptions>>().Value;
                    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
                });

                var jwtKey = builder.Configuration["Jwt:Key"];
                var jwtIssuer = builder.Configuration["Jwt:Issuer"];

                builder.Services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = false,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtIssuer,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                    };
                });

                var app = builder.Build();

                // Configure the HTTP request pipeline.
                if (app.Environment.IsDevelopment())
                {
                    app.UseSwagger();
                    app.UseSwaggerUI();
                    app.UseDeveloperExceptionPage();
                    app.UseMigrationsEndPoint();
                }
                else
                    app.UseExceptionHandler("/Error");

                using (var scope = app.Services.CreateScope())
                {
                    var services = scope.ServiceProvider;
                    var context = services.GetRequiredService<PennyPincherApiDbContext>();
                    await context.Database.MigrateAsync();
                }

                app.UseAuthentication();
                app.UseAuthorization();
                app.MapControllers();
                app.MapHealthChecks("/healthz");

                app.Run();
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message, "Stopped program because of exception");
            }
            finally
            {
                LogManager.Shutdown();
            }
        }
    }
}