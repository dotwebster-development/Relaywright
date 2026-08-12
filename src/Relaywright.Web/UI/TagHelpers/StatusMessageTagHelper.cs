using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Relaywright.Web.UI.TagHelpers;

[HtmlTargetElement("rw-status-message")]
public sealed class StatusMessageTagHelper : TagHelper
{
    public string? Message { get; set; }

    public string Kind { get; set; } = "ok";

    public bool Dismissible { get; set; } = true;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(Message))
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "div";
        output.Attributes.SetAttribute("class", $"status {Kind}");
        output.Attributes.SetAttribute("role", Kind == "error" ? "alert" : "status");
        if (Dismissible)
        {
            output.Attributes.SetAttribute("data-dismissible-status", string.Empty);
            output.Content.SetHtmlContent(
                $"<p>{System.Net.WebUtility.HtmlEncode(Message)}</p>" +
                "<button type=\"button\" class=\"status-dismiss\" data-status-dismiss aria-label=\"Dismiss status message\">Dismiss</button>");
        }
        else
        {
            output.Content.SetContent(Message);
        }
    }
}
