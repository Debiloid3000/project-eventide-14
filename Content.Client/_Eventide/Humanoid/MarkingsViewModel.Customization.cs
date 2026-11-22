using System.Collections.Generic;
using System.Linq;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Humanoid;

public sealed partial class MarkingsViewModel
{
    private readonly Dictionary<(ProtoId<OrganCategoryPrototype> Organ, HumanoidVisualLayers Layer), List<Color>> _previousLayerColors = new();

    /// <summary>
    /// Clears transient appearance color history when the editor switches to another character profile.
    /// </summary>
    public void ResetColorMemory()
    {
        _previousColors.Clear();
        _previousLayerColors.Clear();
    }

    private void RememberMarkingColors(
        ProtoId<OrganCategoryPrototype> organ,
        HumanoidVisualLayers layer,
        Marking marking)
    {
        _previousColors[(organ, layer, marking.MarkingId)] = marking.MarkingColors.ToList();
    }

    private static void ApplyRememberedColors(
        List<Color> colors,
        List<Color>? rememberedColors,
        MarkingPrototype markingPrototype)
    {
        if (rememberedColors == null || markingPrototype.ForcedColoring)
            return;

        for (var i = 0; i < Math.Min(colors.Count, rememberedColors.Count); i++)
        {
            // Preserve prototype-defined accents/highlights and other non-editable channels.
            if (markingPrototype.Sprites[i].ForcedColoring)
                continue;

            colors[i] = rememberedColors[i];
        }
    }

    public bool TrySelectMarking(ProtoId<OrganCategoryPrototype> organ,
        HumanoidVisualLayers layer,
        ProtoId<MarkingPrototype> markingId)
    {
        if (!_prototype.TryIndex(markingId, out var markingProto))
            return false;

        if (!_organData.TryGetValue(organ, out var organData) || !_organProfileData.TryGetValue(organ, out var profileData))
            return false;

        if (!organData.Layers.Contains(layer))
            return false;

        if (!_prototype.TryIndex(organData.Group, out var groupPrototype))
            return false;

        if (EnforceGroupAndSexRestrictions && !_marking.CanBeApplied(organData.Group, profileData.Sex, markingProto))
            return false;

        _markings[organ] = _markings.GetValueOrDefault(organ) ?? [];
        var organMarkings = _markings[organ];
        organMarkings[layer] = organMarkings.GetValueOrDefault(layer) ?? [];
        var layerMarkings = organMarkings[layer];

        var limits = groupPrototype.Limits.GetValueOrDefault(layer);
        var usesLayerPalette = EnforceLimits && limits is { Limit: 1 };
        var layerKey = (organ, layer);

        // A single-choice layer (hair, ears, tail, wings, etc.) carries its editable
        // palette to the replacement style. Do not seed that palette from a forced style.
        if (usesLayerPalette &&
            layerMarkings.FirstOrDefault() is { } currentMarking &&
            IsMarkingColorCustomizable(organ, layer, currentMarking.MarkingId))
        {
            RememberMarkingColors(organ, layer, currentMarking);
            _previousLayerColors[layerKey] = currentMarking.MarkingColors.ToList();
        }

        var colors = MarkingColoring.GetMarkingLayerColors(markingProto, profileData.SkinColor, profileData.EyeColor, layerMarkings);
        var canCustomizeColors = IsMarkingColorCustomizable(organ, layer, markingId);
        var rememberedColors = usesLayerPalette
            ? _previousLayerColors.GetValueOrDefault(layerKey)
            : _previousColors.GetValueOrDefault((organ, layer, markingId));

        if (canCustomizeColors)
            ApplyRememberedColors(colors, rememberedColors, markingProto);

        var newMarking = new Marking(markingId, colors) { Forced = AnyEnforcementsLifted };

        if (limits is null || !EnforceLimits)
        {
            layerMarkings.Add(newMarking);
            RememberMarkingColors(organ, layer, newMarking);
            MarkingsChanged?.Invoke(organ, layer);
            return true;
        }

        if (limits.Limit == 1 && layerMarkings.Count == 1)
        {
            layerMarkings.Clear();
            layerMarkings.Add(newMarking);
            RememberMarkingColors(organ, layer, newMarking);

            if (canCustomizeColors)
                _previousLayerColors[layerKey] = newMarking.MarkingColors.ToList();

            MarkingsChanged?.Invoke(organ, layer);
            return true;
        }

        if (layerMarkings.Count < limits.Limit)
        {
            layerMarkings.Add(newMarking);
            RememberMarkingColors(organ, layer, newMarking);

            if (usesLayerPalette && canCustomizeColors)
                _previousLayerColors[layerKey] = newMarking.MarkingColors.ToList();

            MarkingsChanged?.Invoke(organ, layer);
            return true;
        }

        return false;
    }

    public bool TryDeselectMarking(ProtoId<OrganCategoryPrototype> organ,
        HumanoidVisualLayers layer,
        ProtoId<MarkingPrototype> markingId)
    {
        if (!_organData.TryGetValue(organ, out var organData))
            return false;

        if (!organData.Layers.Contains(layer))
            return false;

        if (!_prototype.TryIndex(organData.Group, out var groupPrototype))
            return false;

        var limits = groupPrototype.Limits.GetValueOrDefault(layer);

        _markings[organ] = _markings.GetValueOrDefault(organ) ?? [];
        var organMarkings = _markings[organ];
        organMarkings[layer] = organMarkings.GetValueOrDefault(layer) ?? [];
        var layerMarkings = organMarkings[layer];

        var count = layerMarkings.Count(marking => marking.MarkingId == markingId);
        if (count == 0)
            return false;

        if (EnforceLimits && limits is not null && limits.Required && (layerMarkings.Count - count) <= 0)
            return false;

        if (layerMarkings.Find(marking => marking.MarkingId == markingId) is { } removingMarking)
        {
            RememberMarkingColors(organ, layer, removingMarking);

            // A shared layer palette is meaningful only for a single-choice slot.
            // Multi-choice layers keep each marking's own remembered colors instead.
            if (EnforceLimits &&
                limits is { Limit: 1 } &&
                IsMarkingColorCustomizable(organ, layer, markingId))
            {
                _previousLayerColors[(organ, layer)] = removingMarking.MarkingColors.ToList();
            }
        }

        layerMarkings.RemoveAll(marking => marking.MarkingId == markingId);
        MarkingsChanged?.Invoke(organ, layer);

        return true;
    }

    public void TrySetMarkingColor(ProtoId<OrganCategoryPrototype> organ,
        HumanoidVisualLayers layer,
        ProtoId<MarkingPrototype> markingId,
        int colorIndex,
        Color color)
    {
        if (!_markings.TryGetValue(organ, out var markingSet))
            return;

        if (!markingSet.TryGetValue(layer, out var markings))
            return;

        var markingIdx = markings.FindIndex(it => it.MarkingId == markingId);
        if (markingIdx == -1)
            return;

        var markingProto = _prototype.Index(markingId);
        var newColor = color;
        if (markingProto.Strategy != null)
            newColor = markingProto.Strategy.ClosestSkinColor(color);

        markings[markingIdx] = markings[markingIdx].WithColorAt(colorIndex, newColor);
        RememberMarkingColors(organ, layer, markings[markingIdx]);

        if (EnforceLimits &&
            _organData.TryGetValue(organ, out var organData) &&
            _prototype.TryIndex(organData.Group, out var groupPrototype) &&
            groupPrototype.Limits.GetValueOrDefault(layer) is { Limit: 1 } &&
            IsMarkingColorCustomizable(organ, layer, markingId))
        {
            _previousLayerColors[(organ, layer)] = markings[markingIdx].MarkingColors.ToList();
        }

        MarkingsChanged?.Invoke(organ, layer);
    }
}
