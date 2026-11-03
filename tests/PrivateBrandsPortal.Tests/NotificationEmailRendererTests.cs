using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Models.Entities;
using PrivateBrandsPortal.Web.Models.Enums;
using PrivateBrandsPortal.Web.Services;
using PrivateBrandsPortal.Web.Interfaces;

namespace PrivateBrandsPortal.Tests;

public sealed class NotificationEmailRendererTests
{
    private static NotificationEmailData Data(string? comment = "Proszę o decyzję.") =>
        new("PB-2026-4873", "ALDI", "Poland", "Anna Nowak", "Jan Kowalski", "SKU-6", "HAIR CARE", "HAIR SERUM", comment);
    private static NotificationOutbox Row(string type = "ProductApproved", NotificationEmailData? data = null) => new()
    {
        EventKey = "example-only", EventType = type, Subject = "legacy technical subject", Body = NotificationEmailRenderer.Serialize(data ?? Data()),
        ProjectId = 123, ProductId = 6, RecipientEmail = "example@example.invalid", RelativePath = "Projects/Details/123",
        CreatedAtUtc = new DateTimeOffset(2026, 11, 3, 13, 32, 0, TimeSpan.Zero)
    };

    [Fact] public void Polish_content_contains_snapshot_and_local_date_and_link()
    {
        var result = NotificationEmailRenderer.Render(Row(), new());
        Assert.Equal("PB-2026-4873 — produkt został zatwierdzony", result.Subject);
        foreach (var value in new[] { "PB-2026-4873", "ALDI", "Poland", "Anna Nowak", "Jan Kowalski", "SKU-6", "HAIR CARE", "HAIR SERUM", "Proszę o decyzję.", "3 listopada 2026, 14:32" })
        {
            Assert.Contains(value, result.TextBody);
            Assert.Contains(WebUtility.HtmlEncode(value), result.HtmlBody);
        }
        Assert.Contains("href=\"https://portalmp.forte.local/Projects/Details/123\"", result.HtmlBody);
        Assert.DoesNotContain("ProductApproved", result.Subject + result.TextBody + result.HtmlBody);
        Assert.DoesNotContain("2026-11-03T", result.TextBody);
    }

    [Theory][InlineData(null)][InlineData("")][InlineData("   ")]
    public void Empty_comment_and_product_fields_are_hidden(string? comment)
    {
        var result = NotificationEmailRenderer.Render(Row(data: Data(comment) with { SKU = null, Category = null, Subcategory = null }), new());
        Assert.DoesNotContain("Komentarz / powód", result.TextBody + result.HtmlBody);
        Assert.DoesNotContain("SKU", result.TextBody + result.HtmlBody);
        Assert.DoesNotContain("Podkategoria", result.TextBody + result.HtmlBody);
    }

    [Fact] public void Whole_project_has_polish_scope_and_action_link()
    {
        var row = Row("ProjectSubmitted"); row.ProductId = null; row.RelativePath = "Approvals/Review/123";
        var result = NotificationEmailRenderer.Render(row, new());
        Assert.Contains("Zakres: Cały projekt", result.TextBody);
        Assert.DoesNotContain("Whole project", result.TextBody + result.HtmlBody);
        Assert.DoesNotContain("SKU-6", result.TextBody + result.HtmlBody);
        Assert.Contains("https://portalmp.forte.local/Approvals/Review/123", result.HtmlBody);
    }

    [Fact] public void All_user_fields_are_encoded_and_comment_line_breaks_preserved()
    {
        const string attack = "<script>alert(\"x\")</script>&'";
        var data = new NotificationEmailData(attack, attack, attack, attack, attack, attack, attack, attack, attack + "\nnext");
        var result = NotificationEmailRenderer.Render(Row(data: data), new());
        Assert.DoesNotContain("<script>", result.HtmlBody);
        Assert.Equal(9, result.HtmlBody!.Split(WebUtility.HtmlEncode(attack)).Length - 1);
        Assert.Contains("<br>next", result.HtmlBody);
        Assert.Contains(attack, result.TextBody);
    }

    [Fact] public void Legacy_snapshot_keeps_multiline_comment_without_technical_event()
    {
        var row = Row();
        row.Body = "Project: PB-2026-4873\nCustomer: ALDI\nEvent: ProductApproved\nActor: Jan\nSKU: 6\nComment / reason: Approved\nActor: still comment\nOccurred (UTC): 2026-11-03T13:32:00Z";
        var result = NotificationEmailRenderer.Render(row, new());
        Assert.Contains("Approved\nActor: still comment", result.TextBody);
        Assert.DoesNotContain("ProductApproved", result.TextBody + result.HtmlBody);
        Assert.DoesNotContain("Kraj:", result.TextBody);
        row.Body = row.Body.Replace("Approved\nActor: still comment", "—");
        Assert.DoesNotContain("Komentarz / powód", NotificationEmailRenderer.Render(row, new()).TextBody);
    }

    [Fact] public void Summer_time_is_used()
    {
        var row = Row(); row.CreatedAtUtc = new DateTimeOffset(2026, 7, 3, 12, 32, 0, TimeSpan.Zero);
        Assert.Contains("3 lipca 2026, 14:32", NotificationEmailRenderer.Render(row, new()).TextBody);
    }

    [Fact] public void Every_workflow_event_and_commercial_status_has_a_polish_label()
    {
        var events = new[] { "ProjectSubmitted", "ProjectApprovalBypassed", "ImplementationRequested", "ImplementationResubmitted", "ImplementationApproved", "ImplementationRejected", "ProductRejectedByCustomer", "ProjectManagerChanged" }
            .Concat(Enum.GetValues<ReviewDecision>().Select(x => "Product" + x))
            .Concat(Enum.GetValues<CommercialStatus>().Select(x => "CommercialStatus:" + x));
        foreach (var type in events)
        {
            Assert.True(NotificationEmailRenderer.Titles.ContainsKey(type), type);
            var result = NotificationEmailRenderer.Render(Row(type), new());
            Assert.DoesNotContain(type, result.Subject + result.TextBody + result.HtmlBody);
        }
    }

    [Fact] public void Unknown_event_uses_safe_polish_fallback()
    {
        var result = NotificationEmailRenderer.Render(Row("FutureInternalEvent"), new());
        Assert.Contains("projekt został zaktualizowany", result.Subject);
        Assert.DoesNotContain("FutureInternalEvent", result.TextBody + result.HtmlBody);
    }

    [Theory][InlineData("https://evil.example/path")][InlineData("javascript:alert(1)")]
    public void External_or_script_links_are_rejected(string path)
    {
        var row = Row(); row.RelativePath = path;
        Assert.Throws<InvalidOperationException>(() => NotificationEmailRenderer.Render(row, new()));
    }

    [Fact] public void Invalid_time_zone_is_reported_in_configuration_validation()
    {
        Assert.Contains(EmailProviderConfiguration.Errors(new() { TimeZone = "invalid-zone" }, new()), x => x.Contains("TimeZone"));
    }

    [Fact] public async Task Simulation_accepts_both_bodies_and_exports_fake_previews_without_email()
    {
        var sender = new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance);
        var directory = Path.Combine(AppContext.BaseDirectory, "email-previews");
        Directory.CreateDirectory(directory);
        foreach (var (name, type) in new[] { ("manager", "ProjectSubmitted"), ("pm", "ProductApproved") })
        {
            var row = Row(type, Data(name == "manager" ? null : "Zatwierdzam produkt zgodnie z przedstawioną kalkulacją."));
            if (name == "manager") { row.ProductId = null; row.RelativePath = "Approvals/Review/123"; }
            var message = NotificationEmailRenderer.Render(row, new());
            Assert.Equal(EmailSendResult.Simulated, await sender.SendAsync(message, default));
            await File.WriteAllTextAsync(Path.Combine(directory, name + ".html"), message.HtmlBody);
            await File.WriteAllTextAsync(Path.Combine(directory, name + ".txt"), message.Subject + "\n\n" + message.TextBody);
        }
    }
}
