using System.Text.Json;
using PlayOffsApi.DTO;

namespace PlayOffsApi.Services;

public class CaptchaService
{
    private readonly string _secretKey;
    public CaptchaService()
    {
        _secretKey = Environment.GetEnvironmentVariable("CAPTCHA_KEY");
    }
    
    public async Task<bool> VerifyValidityCaptcha(string captcha)
    {
        if (IsDevelopmentBypassEnabled())
            return true;

        if (string.IsNullOrWhiteSpace(_secretKey) || string.IsNullOrWhiteSpace(captcha))
            return false;

        using var httpClient = new HttpClient();
        var apiUrl = $"https://www.google.com/recaptcha/api/siteverify?secret={_secretKey}&response={captcha}";

        var httpResponse = await httpClient.PostAsync(apiUrl, null); // PostAsync will make a POST request to the given URL.
        if (!httpResponse.IsSuccessStatusCode)
            return false; // If there's an error in the request, assume it's invalid.
        

        var jsonResponse = await httpResponse.Content.ReadAsStringAsync();
        var recaptchaObj = JsonSerializer.Deserialize<RecaptchaResponseDTO>(jsonResponse);
        
        return recaptchaObj.success;
    }

    private static bool IsDevelopmentBypassEnabled()
    {
        var isDevelopment = Environment.GetEnvironmentVariable("IS_DEVELOPMENT");
        return string.Equals(isDevelopment, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
    }
}