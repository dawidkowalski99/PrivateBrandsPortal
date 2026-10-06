using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Controllers;

[ServiceFilter(typeof(ProjectAccessFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProjectsController(IProjectService projects, IAppUserService users, WizardStore store, IApprovalService approvals, ProductCopyService copies, ProjectDictionaryService lookups) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct, string? search = null) {
        if(search?.Length>200)return BadRequest();
        ViewData["Search"]=search; ViewData["Global"]=(await users.GetCurrentAsync(ct)).Role==Models.Enums.AppRole.SuperAdmin;
        return View(await projects.ListAsync(ct,search));
    }
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var model = await projects.DetailsAsync(id, ct);
        if(model is null && (await users.GetCurrentAsync(ct)).Role==Models.Enums.AppRole.SuperAdmin)
            return RedirectToAction("Project","Dashboard",new{id});
        return model is null ? NotFound() : View(model);
    }
    [HttpGet]
    public async Task<IActionResult> Submit(int id, CancellationToken ct)
    {
        var project = await projects.DetailsAsync(id, ct);
        return project is null || project.Status != Models.Enums.ProjectStatus.Draft ? NotFound() : View(project);
    }
    [HttpPost, ActionName("Submit")]
    public async Task<IActionResult> SubmitConfirmed(int id, DateTimeOffset? version, CancellationToken ct)
    {
        if (!version.HasValue || !ModelState.IsValid) return BadRequest();
        try { await approvals.SubmitAsync(id, version.Value, ct); TempData["Success"] = "Project submitted for manager review."; }
        catch (ValidationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var user = await users.GetCurrentAsync(ct);
        return RedirectToAction(nameof(Wizard), new { token = store.Create(user.Id) });
    }
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var draft = await projects.LoadDraftAsync(id, ct);
        if (draft is null) return NotFound();
        var user = await users.GetCurrentAsync(ct);
        return RedirectToAction(nameof(Wizard), new { token = store.Create(user.Id, draft) });
    }
    private async Task<IActionResult> With(Guid token, CancellationToken ct, Func<WizardState, Task<IActionResult>> action)
    {
        var user = await users.GetCurrentAsync(ct);
        return await store.WithAsync(token, user.Id, async state => {
            if (!state.SavedProjectId.HasValue && state.Draft.ProjectId is int id && await projects.LoadDraftAsync(id, ct) is null)
                return (IActionResult)Forbid();
            return await action(state);
        }, ct) ?? View("Expired");
    }
    private async Task<IActionResult> Page(WizardState state, int step, CancellationToken ct,
        ProductInput? product = null, bool showProduct = false)
    {
        var user = await users.GetCurrentAsync(ct);
        var draft = WizardStore.Snapshot(state.Draft);
        return View("Wizard", new WizardViewModel {
            Token = state.Token, Revision = state.Revision, Step = step, Draft = draft, Brief = draft.Brief,
            Product = product ?? new(), ShowProductForm = showProduct, ProjectManager = user.DisplayName,
            Customers = await lookups.CustomersAsync(ct), Subcategories = await lookups.SubcategoriesAsync(ct),
            Countries = await projects.CountriesAsync(ct), ProductTypes = await projects.ProductCategoriesAsync(ct)
        });
    }
    private IActionResult Next(WizardState state, int step) =>
        RedirectToAction(nameof(Wizard), new { token = state.Token, step });
    private bool Fresh(WizardState state, int revision)
    {
        if (revision == state.Revision) return true;
        ModelState.Clear();
        ModelState.AddModelError("", "This wizard changed in another tab. Review the latest values and try again.");
        return false;
    }
    public Task<IActionResult> Wizard(Guid token, int step = 1, Guid? edit = null, bool add = false, bool copy = false, string? search = null, CancellationToken ct = default) =>
        With(token, ct, async state => {
            if (state.SavedProjectId is int saved) return RedirectToAction(nameof(Details), new { id = saved });
            step = Math.Clamp(step, 1, 3);
            if (!state.BriefCompleted) step = 1;
            if (step == 3 && state.Draft.Products.Count == 0) {
                ModelState.AddModelError("", "Add at least one product before Summary."); step = 2;
            }
            var product = edit.HasValue ? state.Draft.Products.SingleOrDefault(x => x.Key == edit.Value) : null;
            if (edit.HasValue && product is null) return NotFound();
            if(copy && step==2) {
                if(search?.Length>100)return BadRequest();
                ViewData["CopySources"]=await copies.SourcesAsync(search,ct);
                ViewData["CopySearch"]=search;
            }
            return await Page(state, step, ct, product is null ? null : WizardStore.Snapshot(new DraftInput { Products = [product] }).Products[0], add || edit.HasValue);
        });
    [HttpPost]
    public Task<IActionResult> Brief(Guid token, int revision, [Bind(Prefix = "Brief")] BriefInput input, CancellationToken ct) =>
        With(token, ct, async state => {
            if (state.SavedProjectId.HasValue) return RedirectToAction(nameof(Details), new { id = state.SavedProjectId });
            if (!Fresh(state, revision)) return await Page(state, 1, ct);
            input.Customer = (input.Customer ?? "").Trim();
            try { await lookups.ResolveBriefAsync(input, state.Draft.ProjectId.HasValue ? state.Draft.Brief : null, ct); }
            catch (ValidationException ex) { ModelState.AddModelError("Brief.CustomerId", ex.Message); }
            if (!(await projects.CountriesAsync(ct)).Any(x => x.Id == input.CountryId))
                ModelState.AddModelError("Brief.CountryId", "Select an active country.");
            if (!ModelState.IsValid) return await Page(state, 1, ct);
            state.Draft.Brief = input; state.BriefCompleted = true; state.Revision++;
            return Next(state, 2);
        });
    [HttpPost]
    public Task<IActionResult> Product(Guid token, int revision, [Bind(Prefix = "Product")] ProductInput input, CancellationToken ct) =>
        With(token, ct, async state => {
            if (state.SavedProjectId.HasValue) return RedirectToAction(nameof(Details), new { id = state.SavedProjectId });
            if (!state.BriefCompleted) return Next(state, 1);
            if (!Fresh(state, revision)) return await Page(state, 2, ct);
            input.SKU = (input.SKU ?? "").Trim(); input.Subcategory = (input.Subcategory ?? "").Trim();
            var existing = state.Draft.Products.SingleOrDefault(x => x.Key == input.Key);
            input.PersistedId = existing?.PersistedId; // Never accept database IDs from the browser.
            if (existing?.PersistedId.HasValue == true && input.ProductCategoryId == existing.ProductCategoryId)
                ModelState.Remove("Product.ProductCategoryId");
            try { await lookups.ResolveProductAsync(input, existing?.PersistedId.HasValue == true ? existing : null, ct); }
            catch (ValidationException ex) { ModelState.AddModelError("Product.ProductSubcategoryId", ex.Message); }
            if (existing is null && state.Draft.Products.Count >= 500) ModelState.AddModelError("", "A draft supports up to 500 products.");
            if (!ModelState.IsValid) return await Page(state, 2, ct, input, true);
            if (existing is not null) state.Draft.Products[state.Draft.Products.IndexOf(existing)] = input;
            else state.Draft.Products.Add(input);
            state.Revision++;
            return Next(state, 2);
        });
    [HttpPost]
    public Task<IActionResult> Remove(Guid token, int revision, Guid key, CancellationToken ct) =>
        With(token, ct, async state => {
            if (state.SavedProjectId.HasValue) return RedirectToAction(nameof(Details), new { id = state.SavedProjectId });
            if (!Fresh(state, revision)) return await Page(state, 2, ct);
            var product = state.Draft.Products.SingleOrDefault(x => x.Key == key);
            if (product is null) return NotFound();
            state.Draft.Products.Remove(product); state.Revision++;
            return Next(state, 2);
        });
    [HttpPost]
    public Task<IActionResult> Copy(Guid token, int revision, Guid? key, int? sourceId, CancellationToken ct) =>
        With(token, ct, async state => {
            if(state.SavedProjectId.HasValue)return RedirectToAction(nameof(Details),new{id=state.SavedProjectId});
            if(!state.BriefCompleted)return Next(state,1);
            if(!Fresh(state,revision))return await Page(state,2,ct);
            if(state.Draft.Products.Count>=500){ModelState.AddModelError("","A draft supports up to 500 products.");return await Page(state,2,ct);}
            ProductInput? copy=null;
            if(key.HasValue && !sourceId.HasValue){var source=state.Draft.Products.SingleOrDefault(x=>x.Key==key);if(source is not null)copy=ProductCopyService.Duplicate(source);}
            else if(sourceId.HasValue && !key.HasValue)copy=await copies.CopyAsync(sourceId.Value,ct);
            if(copy is null)return NotFound();
            if(!(await projects.ProductCategoriesAsync(ct)).Any(x=>x.Id==copy.ProductCategoryId))copy.ProductCategoryId=null;
            if(!(await lookups.SubcategoriesAsync(ct)).Any(x=>x.Id==copy.ProductSubcategoryId && x.CategoryId==copy.ProductCategoryId))copy.ProductSubcategoryId=null;
            ModelState.Clear();
            return await Page(state,2,ct,copy,true);
        });
    [HttpPost]
    public Task<IActionResult> Save(Guid token, int revision, CancellationToken ct) =>
        With(token, ct, async state => {
            // Replayed/double Save posts reuse the completed result.
            if (state.SavedProjectId is int saved) return RedirectToAction(nameof(Details), new { id = saved });
            if (!Fresh(state, revision)) return await Page(state, 3, ct);
            if (!state.BriefCompleted) return Next(state, 1);
            try {
                var id = await projects.SaveDraftAsync(state.Draft, ct);
                state.SavedProjectId = id; state.Revision++;
                var result = await projects.DetailsAsync(id, ct);
                TempData["Success"] = $"Project {result!.ProjectNumber} saved as draft.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (ValidationException ex) { ModelState.AddModelError("", ex.Message); return await Page(state, 3, ct); }
        });
}
