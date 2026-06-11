using System.Text.Json;
using PlayOffsApi.API;
using PlayOffsApi.Services;
using Resource = PlayOffsApi.Resources.Generic;

namespace PlayOffsApi.Middleware;

public class ErrorMiddleware
{
    private readonly RequestDelegate _next;
    // private readonly ErrorLogService _errorLogService;

    public ErrorMiddleware(RequestDelegate next) // , ErrorLogService errorLogService
    {
        _next = next;
        // _errorLogService = errorLogService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            try
            {
                var errorLogService = context.RequestServices.GetRequiredService<ErrorLogService>();
                await errorLogService.HandleExceptionValidationAsync(context, ex);
            }
            catch (Exception logEx)
            {
                Console.Error.WriteLine("Failed to write error to errorlog: " + logEx);
                Console.Error.WriteLine("Original exception: " + ex);
            }

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";

            var response = new ApiResponse<string>
            {
                Succeed = false,
                Message = Resource.GenericErrorMessage
            };

            var responseString = JsonSerializer.Serialize(response);
            await context.Response.WriteAsync(responseString);
        }
    }
}