using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Relaywright.Web.UI.TagHelpers;

[HtmlTargetElement("rw-table-shell")]
public sealed class TableShellTagHelper : TagHelper
{
    [HtmlAttributeName("accessible-name")]
    public string AccessibleName { get; set; } = "Data table";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.Attributes.SetAttribute("class", "table-wrap");
        output.Attributes.SetAttribute("role", "region");
        output.Attributes.SetAttribute("aria-label", AccessibleName);
        output.Attributes.SetAttribute("tabindex", "0");
    }
}
