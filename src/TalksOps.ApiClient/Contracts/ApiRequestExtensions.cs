namespace TalksOps.ApiClient.Contracts;

internal static class ApiRequestExtensions
{
    public static string WithUserId(this string path, string userId)
    {
        var separator = path.Contains('?') ? '&' : '?';
        return $"{path}{separator}userId={Uri.EscapeDataString(userId)}";
    }
}