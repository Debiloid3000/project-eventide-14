using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using JetBrains.Annotations;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.RichText;

/// <summary>
/// Renders the inline texture tag used by formatted radio headers.
/// </summary>
[UsedImplicitly]
public sealed class TextureTag : IMarkupTagHandler
{
    [Dependency] private IResourceCache _resourceCache = default!;

    public string Name => "texture";

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (!node.Attributes.TryGetValue("path", out var pathParam) ||
            !pathParam.TryGetString(out var path) ||
            string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!_resourceCache.TryGetResource<TextureResource>(new ResPath(path), out var textureResource) ||
            textureResource == null)
        {
            return false;
        }

        var scale = 1f;
        if (node.Attributes.TryGetValue("scale", out var scaleParam) &&
            scaleParam.TryGetString(out var scaleString) &&
            float.TryParse(scaleString, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedScale))
        {
            scale = Math.Clamp(parsedScale, 1f, 4f);
        }

        control = new TextureRect
        {
            Texture = textureResource.Texture,
            TextureScale = new Vector2(scale, scale),
            VerticalAlignment = VAlignment.Center,
        };
        return true;
    }
}
