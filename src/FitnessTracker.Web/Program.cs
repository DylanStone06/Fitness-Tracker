using FitnessTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

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

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
