using System.Diagnostics.CodeAnalysis;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.RichText;

/// <summary>
/// Displays an icon in radio chat, falling back to colored text if the icon cannot be loaded.
/// </summary>
public sealed partial class RadioIconTag : BaseTextureTag
{
    public override string Name => "radicon";

    public override bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (node.Attributes.TryGetValue("path", out var rawPath) &&
            rawPath.TryGetString(out var path))
        {
            long? scaleValue = 3;
            if (node.Attributes.TryGetValue("scale", out var scale) && scale.TryGetLong(out var parsedScale))
                scaleValue = parsedScale;

            if (TryDrawIcon(path, scaleValue.Value, out var texture))
            {
                control = texture;
                return true;
            }
        }

        return TryCreateTextFallback(node, out control);
    }

    private static bool TryCreateTextFallback(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (!node.Attributes.TryGetValue("text", out var rawText) ||
            !rawText.TryGetString(out var text) ||
            !node.Attributes.TryGetValue("color", out var rawColor) ||
            !rawColor.TryGetString(out var color))
        {
            return false;
        }

        control = new Label
        {
            Text = text,
            FontColorOverride = Color.FromHex(color),
        };

        return true;
    }
}
