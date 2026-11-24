using Content.Shared.Humanoid.Prototypes;

namespace Content.Shared.Humanoid.Markings;

public sealed partial class MarkingManager
{
    /// <summary>
    /// Checks the active markings group and its parent groups. This lets specialized species
    /// inherit compatible customization options without copying every parent marking prototype.
    /// </summary>
    private bool IsGroupWhitelisted(MarkingPrototype marking, MarkingsGroupPrototype group)
    {
        if (marking.GroupWhitelist!.Contains(group))
            return true;

        // A layer can explicitly opt out of inherited parent-group markings.
        // Uma uses this only for ears and tails, keeping the rest of Human customization.
        var onlyGroupWhitelisted = group.Limits.GetValueOrDefault(marking.BodyPart)?.OnlyGroupWhitelisted
                                   ?? group.OnlyGroupWhitelisted;
        if (onlyGroupWhitelisted)
            return false;

        foreach (var parentId in group.Parents ?? [])
        {
            var parent = _prototype.Index<MarkingsGroupPrototype>(parentId);
            if (IsGroupWhitelisted(marking, parent))
                return true;
        }

        return false;
    }

}
