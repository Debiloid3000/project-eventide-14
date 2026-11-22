using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Body;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using static Content.Shared.Preferences.HumanoidCharacterProfile;

namespace Content.Shared.Humanoid;

public sealed partial class HumanoidCharacterAppearance
{
    private static Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>> RandomizeAppearanceMarkings(
        SpeciesPrototype species,
        Sex sex,
        CharacterPalette palette,
        IPrototypeManager protoMan,
        IRobustRandom random,
        RandomizeCfg? charEditorRandomizeConfig,
        HumanoidCharacterAppearance? baseAppearance)
    {
        var randomizeCfg = charEditorRandomizeConfig ?? RandomizeConfigAll;

        Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>> markings;
        if (baseAppearance is null)
        {
            markings = RandomizeMarkings(species, sex, palette, protoMan, random);
        }
        else
        {
            // Start from the current appearance, then apply only layers that are both globally
            // enabled and individually unlocked in the randomization settings.
            var randomized = RandomizeMarkings(species, sex, palette, protoMan, random);
            markings = baseAppearance.Markings.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.ToDictionary(layer => layer.Key, layer => layer.Value.ToList()));

            foreach (var organ in markings.Keys.Union(randomized.Keys).ToList())
            {
                var currentLayers = markings.GetValueOrDefault(organ) ?? new();
                var randomLayers = randomized.GetValueOrDefault(organ);
                foreach (var layer in Enum.GetValues<HumanoidVisualLayers>())
                {
                    var randomizeStyle = ShouldRandomizeLayer(layer, randomizeCfg);
                    var randomizeColor = ShouldRandomizeLayerColor(layer, randomizeCfg);
                    if (!randomizeStyle && !randomizeColor)
                        continue;

                    if (randomizeStyle)
                    {
                        if (randomLayers != null && randomLayers.TryGetValue(layer, out var selected))
                        {
                            var selectedMarkings = selected.ToList();
                            if (!randomizeColor)
                                selectedMarkings = PreserveLayerColors(selectedMarkings, currentLayers.GetValueOrDefault(layer));

                            currentLayers[layer] = selectedMarkings;
                        }
                        else
                        {
                            currentLayers.Remove(layer);
                        }
                    }
                    else if (currentLayers.TryGetValue(layer, out var existingMarkings))
                    {
                        currentLayers[layer] = RandomizeLayerColors(existingMarkings.ToList(), layer, palette, protoMan, random);
                    }
                }

                if (currentLayers.Count > 0)
                    markings[organ] = currentLayers;
                else
                    markings.Remove(organ);
            }
        }

        return markings;
    }

    private static bool ShouldRandomizeLayer(HumanoidVisualLayers layer, RandomizeCfg config)
    {
        if ((config & RandomizeCfg.Markings) == 0)
            return false;

        return layer switch
        {
            HumanoidVisualLayers.Hair => (config & RandomizeCfg.Hair) != 0,
            HumanoidVisualLayers.FacialHair => (config & RandomizeCfg.FacialHair) != 0,
            HumanoidVisualLayers.HeadTop => (config & RandomizeCfg.HeadTop) != 0,
            HumanoidVisualLayers.Tail => (config & RandomizeCfg.Tail) != 0,
            _ => (config & RandomizeCfg.BodyMarkings) != 0,
        };
    }

    private static bool ShouldRandomizeLayerColor(HumanoidVisualLayers layer, RandomizeCfg config)
    {
        if ((config & RandomizeCfg.Markings) == 0)
            return false;

        return layer switch
        {
            HumanoidVisualLayers.Hair => (config & RandomizeCfg.HairColor) != 0,
            HumanoidVisualLayers.FacialHair => (config & RandomizeCfg.FacialHairColor) != 0,
            HumanoidVisualLayers.HeadTop => (config & RandomizeCfg.HeadTopColor) != 0,
            HumanoidVisualLayers.Tail => (config & RandomizeCfg.TailColor) != 0,
            _ => (config & RandomizeCfg.BodyMarkingsColor) != 0,
        };
    }

    private static List<Marking> PreserveLayerColors(List<Marking> randomized, List<Marking>? previous)
    {
        if (previous is null || previous.Count == 0)
            return randomized;

        for (var markingIndex = 0; markingIndex < randomized.Count; markingIndex++)
        {
            var oldMarking = previous[Math.Min(markingIndex, previous.Count - 1)];
            var newMarking = randomized[markingIndex];
            for (var colorIndex = 0; colorIndex < Math.Min(oldMarking.MarkingColors.Count, newMarking.MarkingColors.Count); colorIndex++)
                newMarking = newMarking.WithColorAt(colorIndex, oldMarking.MarkingColors[colorIndex]);

            randomized[markingIndex] = newMarking;
        }

        return randomized;
    }

    private static List<Marking> RandomizeLayerColors(
        List<Marking> markings,
        HumanoidVisualLayers layer,
        CharacterPalette palette,
        IPrototypeManager protoMan,
        IRobustRandom random)
    {
        for (var markingIndex = 0; markingIndex < markings.Count; markingIndex++)
        {
            var marking = markings[markingIndex];
            if (!protoMan.TryIndex(marking.MarkingId, out MarkingPrototype? prototype))
                continue;

            // Hair and facial hair use the dedicated hair-color palette in the regular randomizer.
            if (layer is HumanoidVisualLayers.Hair or HumanoidVisualLayers.FacialHair)
            {
                markings[markingIndex] = marking.WithColor(palette.HairColor);
                continue;
            }

            for (var colorIndex = 0; colorIndex < Math.Min(prototype.Sprites.Count, marking.MarkingColors.Count); colorIndex++)
            {
                var layerName = prototype.Sprites[colorIndex].GetLayerStateId();
                var coloringType = (prototype.Coloring.Layers is not { } layers ||
                                    !layers.TryGetValue(layerName, out var layerColoring))
                    ? prototype.Coloring.Default
                    : layerColoring;

                var color = coloringType.Type is not null
                    ? coloringType.GetColor(palette.SkinColor, palette.EyeColor, markings)
                    : random.Pick(new List<Color> { palette.HairColor, palette.EyeColor });

                marking = marking.WithColorAt(colorIndex, color);
            }

            markings[markingIndex] = marking;
        }

        return markings;
    }

}
