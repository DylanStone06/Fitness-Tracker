using FitnessTracker.Web.Data;
using FitnessTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllersWithViews(options =>
    {
        // Requirements §9: every form post must carry the anti-forgery token the page gave it.
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    })
    .AddViewOptions(options =>
    {
        // Validation happens on the server; there is no client-side validation library to use these attributes.
        options.HtmlHelperOptions.ClientValidationEnabled = false;
    });

// ACC-1: accounts are an email address and a password.
builder.Services
    .AddIdentity<AppUser, IdentityRole>(options =>
    {
        // Length rather than forced digits, capitals or symbols: a few words together make a strong password.
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;

        options.User.RequireUniqueEmail = true;
        // The MVP sends no email, so accounts can't be confirmed by email (a documented limitation).
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>();

// ACC-2: every page needs a signed-in user unless it is explicitly marked [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Who is signed in, for AppDbContext's per-user filtering (CLAUDE.md rule 5).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// The connection string is read when a database context is first needed, after all configuration
// (including test overrides) has been applied. In development, dev\ft-dev.ps1 run keeps it in user-secrets.
builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "No database is configured: set ConnectionStrings:Default. " +
            @"For local development, run dev\ft-dev.ps1 run, which stores one in user-secrets.");
    }

    options.UseSqlite(connectionString);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Development only: bring the local database up to date on start, saving a copy first if it holds data.
    // How a hosted database is migrated is decided with hosting, in Phase 9.
    using var scope = app.Services.CreateScope();
    var backupPath = DevelopmentDatabase.MigrateWithBackup(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    if (backupPath is not null)
    {
        app.Logger.LogInformation("Saved a copy of the development database before migrating it: {BackupPath}", backupPath);
    }
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Responses with an error status and no body (404, a rejected form's 400) get the app's own page,
// keeping their status code.
app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// CSS, scripts and images hold nothing private, and the sign-in page needs them before anyone has signed in.
app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
