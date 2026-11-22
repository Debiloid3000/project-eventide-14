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

        // Preserve the palette when replacing a single-choice layer (hair, ears, tails, wings).
        if (limits is { Limit: 1 } && layerMarkings.FirstOrDefault() is { } currentMarking)
            _previousLayerColors[(organ, layer)] = currentMarking.MarkingColors.ToList();

        var colors = MarkingColoring.GetMarkingLayerColors(markingProto, profileData.SkinColor, profileData.EyeColor, layerMarkings);
        var rememberedColors = _previousLayerColors.GetValueOrDefault((organ, layer))
                               ?? _previousColors.GetValueOrDefault(markingId);
        if (rememberedColors != null)
        {
            for (var i = 0; i < Math.Min(colors.Count, rememberedColors.Count); i++)
                colors[i] = rememberedColors[i];
        }

        var newMarking = new Marking(markingId, colors) { Forced = AnyEnforcementsLifted };

        if (limits is null || !EnforceLimits)
        {
            layerMarkings.Add(newMarking);
            if (layerMarkings.Count == 1)
                _previousLayerColors[(organ, layer)] = newMarking.MarkingColors.ToList();
            MarkingsChanged?.Invoke(organ, layer);
            return true;
        }

        if (limits.Limit == 1 && layerMarkings.Count == 1)
        {
            layerMarkings.Clear();
            layerMarkings.Add(newMarking);
            _previousLayerColors[(organ, layer)] = newMarking.MarkingColors.ToList();
            MarkingsChanged?.Invoke(organ, layer);
            return true;
        }

        if (layerMarkings.Count < limits.Limit)
        {
            layerMarkings.Add(newMarking);
            if (limits.Limit == 1 || layerMarkings.Count == 1)
                _previousLayerColors[(organ, layer)] = newMarking.MarkingColors.ToList();
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
            _previousColors[removingMarking.MarkingId] = removingMarking.MarkingColors.ToList();
            _previousLayerColors[(organ, layer)] = removingMarking.MarkingColors.ToList();
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
        _previousColors[markingId] = markings[markingIdx].MarkingColors.ToList();
        _previousLayerColors[(organ, layer)] = markings[markingIdx].MarkingColors.ToList();
        MarkingsChanged?.Invoke(organ, layer);
    }

}
