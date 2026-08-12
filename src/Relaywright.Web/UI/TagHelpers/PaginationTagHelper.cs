using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Relaywright.Web.UI.TagHelpers;

[HtmlTargetElement("rw-pagination")]
public sealed class PaginationTagHelper : TagHelper
{
    [HtmlAttributeName("page-number")]
    public int PageNumber { get; set; }

    [HtmlAttributeName("total-pages")]
    public int TotalPages { get; set; }

    [HtmlAttributeName("previous-url")]
    public string? PreviousUrl { get; set; }

    [HtmlAttributeName("next-url")]
    public string? NextUrl { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (TotalPages <= 1)
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "nav";
        output.Attributes.SetAttribute("class", "pagination");
        output.Attributes.SetAttribute("aria-label", "Pagination");

        var links = new List<string>();
        if (!string.IsNullOrWhiteSpace(PreviousUrl))
        {
            links.Add($"<a href=\"{WebUtility.HtmlEncode(PreviousUrl)}\" rel=\"prev\">Previous</a>");
        }

        if (!string.IsNullOrWhiteSpace(NextUrl))
        {
            links.Add($"<a href=\"{WebUtility.HtmlEncode(NextUrl)}\" rel=\"next\">Next</a>");
        }

        output.Content.SetHtmlContent(
            $"<span>Page {PageNumber} of {TotalPages}</span><div>{string.Join(string.Empty, links)}</div>");
    }
}
