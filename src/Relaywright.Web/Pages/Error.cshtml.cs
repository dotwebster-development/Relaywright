using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Relaywright.Web.Pages;

[AllowAnonymous]
public sealed class ErrorModel : PageModel
{
    public string? RequestId { get; private set; }

    public void OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}
