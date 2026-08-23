using JobAlign.Core.Entities.Identity;
using JobAlign.Infrastructure;
using JobAlign.Infrastructure.Data;
using JobAlign.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var connectionString = builder.Configuration.GetConnectionString("JobAlignDb")
    ?? throw new InvalidOperationException("Connection string 'JobAlignDb' was not found.");

// DbContext and the Core service implementations (NFR-11).
builder.Services.AddJobAlignInfrastructure(connectionString);

// Identity provides the salted one-way password hash required by NFR-05.
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.User.RequireUniqueEmail = true;             // FR-01
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<JobAlignDbContext>()
    .AddDefaultTokenProviders();                            // FR-05 password reset tokens

// FR-04: end the session after a period of inactivity.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    options.SlidingExpiration = true;
});

// NFR-04: authentication is the default for every endpoint. Screens that must work
// signed out — the landing page and the account screens — opt out with [AllowAnonymous].
// A fallback policy fails closed: a new controller added without an attribute is
// protected rather than public.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();      // NFR-05: traffic encrypted in transit
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// FR-03: the Candidate and Administrator roles must exist before anyone can be
// assigned one. Idempotent, so running it on every start is safe.
using (var scope = app.Services.CreateScope())
{
    await RoleSeeder.SeedAsync(scope.ServiceProvider);
    
    // FR-14, FR-57, FR-58: Seed master skills and aliases.
    // Idempotent - runs on every startup, checks before inserting.
    var skillSeeder = scope.ServiceProvider.GetRequiredService<MasterSkillSeeder>();
    await skillSeeder.SeedAsync();
}

app.Run();
