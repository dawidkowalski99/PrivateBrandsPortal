namespace PrivateBrandsPortal.Web.Configuration;

public sealed class FileStorageOptions
{
    public string RootPath { get; set; } = "";
    public int MaxFileSizeMb { get; set; } = 25;
    public string[] AllowedExtensions { get; set; } = [".xlsx", ".xlsm", ".xls", ".pdf", ".txt", ".doc", ".docx"];
    public long MaxBytes => checked((long)MaxFileSizeMb * 1024 * 1024);
}
