using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Relaywright.Web.UI.TagHelpers;

[HtmlTargetElement("rw-action-bar")]
public sealed class ActionBarTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.Attributes.SetAttribute("class", "page-action-bar");
    }
}
