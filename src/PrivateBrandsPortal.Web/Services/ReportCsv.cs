using System.Globalization;
using System.Text;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;
public static class ReportCsv
{
    // Quote every cell and neutralize spreadsheet formulas in user-supplied text.
    public static string Cell(string? value){value??="";if(value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@') || value.StartsWith('\t') || value.StartsWith('\r'))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
    public static async Task WriteAsync(Stream stream,IAsyncEnumerable<ReportRow> rows,CancellationToken ct)
    {
        await using var writer=new StreamWriter(stream,new UTF8Encoding(true),65536,leaveOpen:true);
        await writer.WriteLineAsync("Project Number;Project Manager;Customer;Country;Project Status;Category;Subcategory;SKU;Quantity;Estimated Value PLN;Estimated Margin %;Manager Decision;Rejection Reason;Commercial Status;Created UTC;Archived UTC".AsMemory(),ct);
        var culture=CultureInfo.GetCultureInfo("pl-PL");
        await foreach(var r in rows.WithCancellation(ct)){
            var cells=new[]{r.ProjectNumber,r.ProjectManager,r.Customer,r.Country,EnumLabel.Text(r.ProjectStatus),r.Category,r.Subcategory,r.SKU,r.Quantity.ToString(CultureInfo.InvariantCulture),r.EstimatedValue.ToString("F2",culture),r.EstimatedMargin.ToString("F2",culture),EnumLabel.Text(r.ReviewStatus),r.RejectionReason,r.CommercialStatus.HasValue?EnumLabel.Text(r.CommercialStatus.Value):"",r.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"),r.ArchivedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss")};
            await writer.WriteLineAsync(string.Join(';',cells.Select(Cell)).AsMemory(),ct);
        }
        await writer.FlushAsync(ct);
    }
}
