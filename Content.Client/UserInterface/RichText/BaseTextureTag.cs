using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.IoC;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.RichText;

/// <summary>
/// Shared helper for custom rich-text tags that display an icon from an RSI path.
/// Sunrise defines this helper in its own content project; it is not part of RobustToolbox.
/// </summary>
public abstract partial class BaseTextureTag : IMarkupTagHandler
{
    [Dependency] private IEntitySystemManager _entitySystemManager = default!;

    private SpriteSystem? _spriteSystem;

    public abstract string Name { get; }

    public abstract bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control);

    protected bool TryDrawIcon(string path, long scaleValue, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (!TrySplitRsiPath(path, out var rsiPath, out var state))
            return false;

        _spriteSystem ??= _entitySystemManager.GetEntitySystem<SpriteSystem>();

        var texture = new TextureRect
        {
            Texture = _spriteSystem.Frame0(new SpriteSpecifier.Rsi(new ResPath(rsiPath), state)),
            TextureScale = new Vector2(scaleValue, scaleValue),
            VerticalAlignment = Control.VAlignment.Center,
        };

        control = texture;
        return true;
    }

    private static bool TrySplitRsiPath(string fullPath, out string rsiPath, out string state)
    {
        var lastSlash = fullPath.LastIndexOf('/');
        var lastDot = fullPath.LastIndexOf('.');
        if (lastSlash <= 0 || lastDot <= lastSlash + 1)
        {
            rsiPath = string.Empty;
            state = string.Empty;
            return false;
        }

        rsiPath = fullPath[..lastSlash];
        state = fullPath[(lastSlash + 1)..lastDot];
        return !string.IsNullOrWhiteSpace(state);
    }
}
