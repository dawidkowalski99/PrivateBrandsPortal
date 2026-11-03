using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Interfaces;
using PrivateBrandsPortal.Web.Models.Entities;

namespace PrivateBrandsPortal.Web.Services;

public sealed record NotificationEmailData(string ProjectNumber, string? Customer, string? Country,
    string? ProjectManager, string? Actor, string? SKU, string? Category, string? Subcategory, string? Comment);

/// <summary>Presentation only. Versioned snapshots share the existing Outbox Body column.</summary>
public static class NotificationEmailRenderer
{
    private const string Prefix = "notification-email:v1\n";
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");
    public static IReadOnlyDictionary<string, string> Titles { get; } = new Dictionary<string, string>
    {
        ["ProjectSubmitted"] = "Projekt oczekuje na akceptację",
        ["AwaitingManagerReview"] = "Projekt oczekuje na akceptację",
        ["ProjectApprovalBypassed"] = "Projekt przekazano do realizacji bez dodatkowej akceptacji",
        ["ProductApproved"] = "Produkt został zatwierdzony",
        ["ProductRejected"] = "Produkt został odrzucony",
        ["ProductEditedAndApproved"] = "Produkt został zmieniony i zatwierdzony",
        ["EditedAndApproved"] = "Produkt został zmieniony i zatwierdzony",
        ["ImplementationRequested"] = "Produkt oczekuje na akceptację wdrożenia",
        ["ImplementationResubmitted"] = "Produkt został ponownie przekazany do akceptacji wdrożenia",
        ["ImplementationApproved"] = "Wdrożenie produktu zostało zatwierdzone",
        ["ImplementationRejected"] = "Wdrożenie produktu zostało odrzucone",
        ["ProductRejectedByCustomer"] = "Produkt został odrzucony przez klienta",
        ["ProjectManagerChanged"] = "Zmieniono Project Managera projektu",
        ["CommercialStatus:PriceOfferSubmitted"] = "Oferta cenowa została przekazana klientowi",
        ["CommercialStatus:OfferUnderNegotiation"] = "Oferta jest w trakcie negocjacji",
        ["CommercialStatus:CustomerApprovedOrder"] = "Klient zatwierdził projekt lub zamówienie",
        ["CommercialStatus:CustomerNotApproved"] = "Produkt został odrzucony przez klienta",
        ["CommercialStatus:ImplementationIntoProduction"] = "Produkt przekazano do wdrożenia do produkcji",
        ["CommercialStatus:SalesAndDelivery"] = "Produkt przeszedł do sprzedaży i dostawy"
    };

    public static string Title(string eventType) => Titles.GetValueOrDefault(eventType) ?? "Projekt został zaktualizowany";
    public static string Subject(string number, string eventType) => $"{number} — {Title(eventType).ToLower(Polish)}";
    public static string Serialize(NotificationEmailData data) => Prefix + JsonSerializer.Serialize(data);
    private static string Explanation(string eventType) => eventType switch
    {
        "ProjectSubmitted" or "AwaitingManagerReview" => "Projekt został przekazany do przeglądu i oczekuje na decyzję dotyczącą produktów.",
        "ProjectApprovalBypassed" => "Projekt przekazano do dalszej realizacji zgodnie z obowiązującą zasadą pominięcia dodatkowej akceptacji.",
        "ProductApproved" => "Zapisano pozytywną decyzję dotyczącą wskazanego produktu.",
        "ProductRejected" => "Zapisano decyzję o odrzuceniu wskazanego produktu; uzasadnienie znajduje się poniżej.",
        "ProductEditedAndApproved" or "EditedAndApproved" => "Osoba zatwierdzająca zmieniła dane produktu i zaakceptowała jego zaktualizowaną wersję.",
        "ImplementationRequested" => "Wskazany produkt oczekuje na decyzję w sprawie wdrożenia do produkcji.",
        "ImplementationResubmitted" => "Ponownie zgłoszono wskazany produkt do oceny wdrożenia do produkcji.",
        "ImplementationApproved" => "Zapisano zgodę na wdrożenie wskazanego produktu.",
        "ImplementationRejected" => "Zapisano odmowę akceptacji wdrożenia produktu; uzasadnienie znajduje się poniżej.",
        "ProductRejectedByCustomer" or "CommercialStatus:CustomerNotApproved" => "Zarejestrowano odrzucenie wskazanego produktu przez klienta.",
        "ProjectManagerChanged" => "Projekt został przypisany do Project Managera wskazanego poniżej.",
        "CommercialStatus:PriceOfferSubmitted" => "Zarejestrowano przekazanie oferty cenowej klientowi.",
        "CommercialStatus:OfferUnderNegotiation" => "Zmieniono etap produktu na negocjacje oferty z klientem.",
        "CommercialStatus:CustomerApprovedOrder" => "Zarejestrowano akceptację projektu lub zamówienia przez klienta.",
        "CommercialStatus:ImplementationIntoProduction" => "Wskazany produkt rozpoczął etap wdrożenia do produkcji.",
        "CommercialStatus:SalesAndDelivery" => "Wskazany produkt przeszedł do etapu sprzedaży i dostawy.",
        _ => "W projekcie zapisano zmianę; szczegóły są dostępne w portalu."
    };

    public static NotificationEmailData ReadData(NotificationOutbox row)
    {
        if (row.Body.StartsWith(Prefix, StringComparison.Ordinal))
            return JsonSerializer.Deserialize<NotificationEmailData>(row.Body[Prefix.Length..])
                ?? throw new InvalidOperationException("Invalid notification presentation snapshot.");

        // Compatibility with queued messages produced before HTML templates. Do not query mutable
        // project data or rewrite old rows. Comments may contain line breaks and field-like text.
        string? Field(string name)
        {
            var line = row.Body.Split('\n').FirstOrDefault(x => x.StartsWith(name + ": ", StringComparison.Ordinal));
            return line?[(name.Length + 2)..].TrimEnd('\r');
        }
        const string marker = "\nComment / reason: ";
        var start = row.Body.IndexOf(marker, StringComparison.Ordinal);
        var end = row.Body.LastIndexOf("\nOccurred (UTC): ", StringComparison.Ordinal);
        var comment = start >= 0 && end > start ? row.Body[(start + marker.Length)..end] : null;
        return new(Field("Project") ?? row.Subject.Split(" — ")[0], Field("Customer"), null, null,
            Field("Actor"), row.ProductId.HasValue ? Field("SKU") : null, null, null, comment == "—" ? null : comment);
    }

    public static EmailMessage Render(NotificationOutbox row, NotificationOptions options)
    {
        var data = ReadData(row);
        var title = Title(row.EventType);
        var baseUri = new Uri(options.BaseUrl, UriKind.Absolute);
        var link = new Uri(baseUri, row.RelativePath);
        if (baseUri.Scheme is not ("https" or "http") || link.Scheme != baseUri.Scheme || link.Authority != baseUri.Authority)
            throw new InvalidOperationException("Notification link must remain within the portal.");
        var local = TimeZoneInfo.ConvertTime(row.CreatedAtUtc, TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone));
        var date = local.ToString("d MMMM yyyy, HH:mm", Polish);
        var explanation = Explanation(row.EventType);
        var text = new StringBuilder($"Private Brands Portal\n\n{title}\n{explanation}\n\nDane projektu\n");
        var html = new StringBuilder("<!doctype html><html lang=\"pl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"></head><body style=\"margin:0;background-color:#f3f6fa;font-family:Arial,sans-serif;color:#23364d\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\" style=\"padding:24px 12px\"><!--[if mso]><table role=\"presentation\" width=\"600\"><tr><td><![endif]--><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;background-color:#ffffff\"><tr><td style=\"padding:28px\">");
        static string E(string value) => WebUtility.HtmlEncode(value);
        html.Append("<p style=\"color:#087e89;font-size:16px;font-weight:bold;margin:0 0 24px\">Private Brands Portal</p>");
        html.Append($"<h1 style=\"font-size:24px;line-height:32px;margin:0 0 16px\">{E(title)}</h1><p style=\"font-size:15px;line-height:24px\">{E(explanation)}</p><h2 style=\"font-size:17px;margin-top:24px\">Dane projektu</h2><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">");
        void Field(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            text.AppendLine($"{label}: {value}");
            html.Append($"<tr><td width=\"38%\" valign=\"top\" style=\"padding:8px 12px 8px 0;color:#64748b;font-size:14px\">{E(label)}</td><td valign=\"top\" style=\"padding:8px 0;font-size:14px;word-break:break-word\"><strong>{E(value)}</strong></td></tr>");
        }
        Field("Numer projektu", data.ProjectNumber); Field("Klient", data.Customer); Field("Kraj", data.Country);
        Field("Project Manager", data.ProjectManager); Field("Osoba wykonująca akcję", data.Actor); Field("Data i godzina (Polska)", date);
        if (row.ProductId.HasValue)
        {
            html.Append("</table><h2 style=\"font-size:17px;margin-top:24px\">Produkt</h2><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">");
            text.AppendLine("\nProdukt");
            Field("SKU", data.SKU); Field("Kategoria", data.Category); Field("Podkategoria", data.Subcategory);
        }
        else Field("Zakres", "Cały projekt");
        html.Append("</table>");
        if (!string.IsNullOrWhiteSpace(data.Comment))
        {
            text.AppendLine($"\nKomentarz / powód\n{data.Comment}");
            html.Append($"<h2 style=\"font-size:17px;margin-top:24px\">Komentarz / powód</h2><p style=\"font-size:14px;line-height:22px\">{E(data.Comment).Replace("\r\n", "\n").Replace("\n", "<br>")}</p>");
        }
        html.Append($"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:28px\"><tr><td bgcolor=\"#164e80\" style=\"padding:14px 24px\"><a href=\"{E(link.AbsoluteUri)}\" style=\"color:#ffffff;font-size:15px;font-weight:bold;text-decoration:none\">Otwórz projekt</a></td></tr></table><p style=\"font-size:12px;color:#64748b;margin-top:24px\">Automatyczne powiadomienie z Private Brands Portal.</p></td></tr></table><!--[if mso]></td></tr></table><![endif]--></td></tr></table></body></html>");
        text.AppendLine($"\nOtwórz projekt: {link.AbsoluteUri}");
        return new(row.EventKey, row.RecipientEmail!, Subject(data.ProjectNumber, row.EventType), text.ToString(), html.ToString());
    }
}
