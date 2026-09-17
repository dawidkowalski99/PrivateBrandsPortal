using Microsoft.AspNetCore.Mvc;
namespace PrivateBrandsPortal.Web.Controllers;
// Stage 1: authenticated informational shell only. Business endpoints will require app role policies.
public sealed class AdministrationController : Controller { public IActionResult Index() => View(); }
