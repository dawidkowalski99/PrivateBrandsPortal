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
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");
app.Run();

public partial class Program { }

