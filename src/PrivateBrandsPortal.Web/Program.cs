using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.ModelBinderProviders.Insert(0, new DecimalModelBinderProvider());
});
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.SetDefaultCulture("pl-PL").AddSupportedCultures("pl-PL").AddSupportedUICultures("en-US");
    o.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("pl-PL", "en-US");
    o.RequestCultureProviders.Clear();
});
builder.Services.AddScoped<IAppUserService, AppUserService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IApprovalService, ApprovalService>();
builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));
builder.Services.AddSingleton<IFileStorage, FileStorage>();
builder.Services.AddScoped<AttachmentService>();
var maxUploadBytes=builder.Configuration.GetValue<int?>("FileStorage:MaxFileSizeMb") ?? 25;
var maxRequestBytes=checked((long)Math.Clamp(maxUploadBytes,1,1024)*1024*1024*10+1024*1024);
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=maxRequestBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o=>o.MultipartBodyLengthLimit=maxRequestBytes);
builder.Services.AddScoped<CommercialService>();
builder.Services.AddScoped<DictionaryService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<UserAdministrationService>();
builder.Services.AddScoped<SuperAdminBootstrap>();
builder.Services.AddScoped<ProductCopyService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ProjectTransferService>();
builder.Services.AddScoped<ProjectDictionaryService>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionAuthorization>();
builder.Services.AddScoped<ReportService>();
builder.Services.Configure<DemoAccessOptions>(builder.Configuration.GetSection("DemoAccess"));
builder.Services.AddScoped<DemoAccess>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, ManagerAuthorization>();

builder.Services.AddScoped<ProjectAccessFilter>();
builder.Services.AddSingleton<WizardStore>();
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(PortalAuthorization.Configure);
builder.Services.AddPortalDatabase(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IProjectNumberGenerator, ProjectNumberGenerator>();
builder.Services.AddExceptionHandler<PortalExceptionHandler>();

var app = builder.Build();
if(args.Contains("--bootstrap-superadmin",StringComparer.Ordinal))
{
    using var scope=app.Services.CreateScope();
    var login=builder.Configuration["bootstrap-superadmin"] ?? "";
    var name=builder.Configuration["display-name"] ?? "";
    try {
        var id=await scope.ServiceProvider.GetRequiredService<SuperAdminBootstrap>().RunAsync(login,name);
        app.Logger.LogInformation("First SuperAdmin created: {UserId}.",id);
    }
    catch(System.ComponentModel.DataAnnotations.ValidationException ex) {
        app.Logger.LogError("Bootstrap rejected: {Reason}",ex.Message);Environment.ExitCode=1;
    }
    await app.DisposeAsync();return;
}
if (args.Contains("--cleanup-temp-attachments", StringComparer.Ordinal))
{
    var storage = app.Services.GetRequiredService<IFileStorage>();
    var removed = await storage.CleanupTemporaryAsync(DateTimeOffset.UtcNow.AddHours(-24), CancellationToken.None);
    app.Logger.LogInformation("Removed {Count} expired temporary attachments.", removed);
    await app.DisposeAsync();
    return;
}
app.UseExceptionHandler("/Home/Error");
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRequestLocalization();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<PortalAllowListMiddleware>();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");
app.Run();

public partial class Program { }
