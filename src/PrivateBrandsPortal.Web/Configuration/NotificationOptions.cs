using System.ComponentModel.DataAnnotations;
namespace PrivateBrandsPortal.Web.Configuration;

public sealed class NotificationOptions
{
    public string EmailProvider { get; set; } = "Simulated";
    public string FromAddress { get; set; } = "";
    public bool WorkerEnabled { get; set; }
    public string BaseUrl { get; set; } = "https://portalmp.forte.local/";
    [Range(1, 300)] public int PollSeconds { get; set; } = 15;
    [Range(1, 100)] public int BatchSize { get; set; } = 20;
    [Range(1, 20)] public int MaxAttempts { get; set; } = 5;
    [Range(1, 3600)] public int RetrySeconds { get; set; } = 30;
}
