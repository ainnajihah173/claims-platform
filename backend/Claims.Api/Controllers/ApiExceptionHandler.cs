using Claims.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Controllers;

// One place that turns known exceptions into problem details. Anything else stays a 500.
public sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        (int Status, string Title, string Detail)? p = ex switch
        {
            // Input shape is checked by the DTOs (400), so a rule broken here is a clash with the claim's state.
            DomainException => (409, "Action not allowed", ex.Message),
            DbUpdateConcurrencyException => (409, "Claim was changed by someone else", "Reload the claim and try again."),
            BadHttpRequestException b => (b.StatusCode, "Bad request", ex.Message),
            _ => null,
        };
        if (p is not { } v) return false;

        http.Response.StatusCode = v.Status;
        return await problems.TryWriteAsync(new()
        {
            HttpContext = http,
            Exception = ex,
            ProblemDetails = { Status = v.Status, Title = v.Title, Detail = v.Detail },
        });
    }
}
