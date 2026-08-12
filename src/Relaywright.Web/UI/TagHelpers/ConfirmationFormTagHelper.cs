using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Relaywright.Web.UI.TagHelpers;

[HtmlTargetElement("button", Attributes = "rw-confirm")]
public sealed class ConfirmationFormTagHelper : TagHelper
{
    [HtmlAttributeName("rw-confirm")]
    public string Message { get; set; } = "Continue with this action?";

    [HtmlAttributeName("rw-confirm-title")]
    public string Title { get; set; } = "Confirm action";

    [HtmlAttributeName("rw-confirm-action")]
    public string ActionLabel { get; set; } = "Continue";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("rw-confirm");
        output.Attributes.RemoveAll("rw-confirm-title");
        output.Attributes.RemoveAll("rw-confirm-action");
        output.Attributes.SetAttribute("data-confirm-button", string.Empty);
        output.Attributes.SetAttribute("data-confirm-title", Title);
        output.Attributes.SetAttribute("data-confirm-message", Message);
        output.Attributes.SetAttribute("data-confirm-action", ActionLabel);
    }
}
