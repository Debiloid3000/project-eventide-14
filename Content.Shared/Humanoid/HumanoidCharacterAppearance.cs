using System.Numerics;
using Content.Shared.Body;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using static Content.Shared.Preferences.HumanoidCharacterProfile;

namespace Content.Shared.Humanoid;

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class HumanoidCharacterAppearance : IEquatable<HumanoidCharacterAppearance>
{
    [DataField]
    public Color EyeColor { get; set; } = Color.Black;

    [DataField]
    public Color SkinColor { get; set; } = Color.FromHsv(new Vector4(0.07f, 0.2f, 1f, 1f));

    [DataField]
    public Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>> Markings { get; set; } = new();

    public HumanoidCharacterAppearance(
        Color eyeColor,
        Color skinColor,
        Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>> markings)
    {
        EyeColor = ClampColor(eyeColor);
        SkinColor = ClampColor(skinColor);
        Markings = markings;
    }

    public HumanoidCharacterAppearance(HumanoidCharacterAppearance other) :
        this(other.EyeColor, other.SkinColor, new(other.Markings))
    {

    }

    public HumanoidCharacterAppearance WithEyeColor(Color newColor)
    {
        return new(newColor, SkinColor, Markings);
    }

    public HumanoidCharacterAppearance WithSkinColor(Color newColor)
    {
        return new(EyeColor, newColor, Markings);
    }

    public HumanoidCharacterAppearance WithMarkings(Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>> newMarkings)
    {
        return new(EyeColor, SkinColor, newMarkings);
    }

    public static HumanoidCharacterAppearance DefaultWithSpecies(ProtoId<SpeciesPrototype> species, Sex sex)
    {
        var protoMan = IoCManager.Resolve<IPrototypeManager>();
        var speciesPrototype = protoMan.Index(species);
        var skinColoration = protoMan.Index(speciesPrototype.SkinColoration).Strategy;
        var skinColor = skinColoration.InputType switch
        {
            SkinColorationStrategyInput.Unary => skinColoration.FromUnary(speciesPrototype.DefaultHumanSkinTone),
            SkinColorationStrategyInput.Color => skinColoration.ClosestSkinColor(speciesPrototype.DefaultSkinTone),
            _ => skinColoration.ClosestSkinColor(speciesPrototype.DefaultSkinTone),
        };

        var appearance = new HumanoidCharacterAppearance(
            Color.Black,
            skinColor,
            new()
        );
        return EnsureValid(appearance, species, sex, []); // Corvax-Sponsors-Edit
    }

    private static readonly IReadOnlyList<Color> RealisticEyeColors =
    [
        Color.Brown,
        Color.Gray,
        Color.Azure,
        Color.SteelBlue,
        Color.Black
    ];

    /// <summary>
    /// Picks a random eye color.
    /// </summary>
    public static Color RandomEyes()
    {
        var random = IoCManager.Resolve<IRobustRandom>();

        var eyes = random.Pick(RealisticEyeColors);
        return eyes;
    }

    /// <summary>
    /// Picks a random skin color using species.
    /// </summary>
    public static Color RandomSkin(ProtoId<SpeciesPrototype> species)
    {
        var random = IoCManager.Resolve<IRobustRandom>();
        var protoMan = IoCManager.Resolve<IPrototypeManager>();

        var speciesProto = protoMan.Index(species);
        var skinType = speciesProto.SkinColoration;
        var strategy = protoMan.Index(skinType).Strategy;

        var skinColor = strategy.InputType switch
        {
            SkinColorationStrategyInput.Unary => strategy.FromUnary(random.NextFloat(0f, 100f)),
            SkinColorationStrategyInput.Color => strategy.ClosestSkinColor(new Color(random.NextFloat(1), random.NextFloat(1), random.NextFloat(1), 1)),
            _ => strategy.ClosestSkinColor(new Color(random.NextFloat(1), random.NextFloat(1), random.NextFloat(1), 1)),
        };

        return skinColor;
    }

    /// <summary>
    ///     Generates a randomized character appearance.
    /// </summary>
    /// <remarks>
    ///     When <see cref="RandomizeCfg"/> and an existing <see cref="HumanoidCharacterAppearance"> are passed in,
    ///     values will be selectively randomized with the option to maintain existing values.
    /// </remarks>
    /// <param name="charEditorRandomizeConfig">Which values to randomize.</param>
    /// <param name="baseAppearance">Appearance to base the new appearance on. Values that are not randomized will be taken from this appearance.</param>
    /// <param name="species">Species prototype ID.</param>
    /// <param name="sex">Sex.</param>
    public static HumanoidCharacterAppearance Random(SpeciesPrototype species, Sex sex, RandomizeCfg? charEditorRandomizeConfig = null, HumanoidCharacterAppearance? baseAppearance = null)
    {
        var random = IoCManager.Resolve<IRobustRandom>();
        var protoMan = IoCManager.Resolve<IPrototypeManager>();

        var skinType = protoMan.Index(species.SkinColoration);
        var palette = GetRandomClampedPalette(skinType, random);

        // squash Cfg as necessary
        palette = palette with
        {
            SkinColor = (charEditorRandomizeConfig & RandomizeCfg.Skin) != 0 || baseAppearance is null
                ? palette.SkinColor : baseAppearance.SkinColor,
            EyeColor = (charEditorRandomizeConfig & RandomizeCfg.Eyes) != 0 || baseAppearance is null
                ? palette.EyeColor : baseAppearance.EyeColor
        };

        var randomizeCfg = charEditorRandomizeConfig ?? RandomizeConfigAll;
        var randomizeAnyMarkings = (randomizeCfg & (RandomizeCfg.Markings |
            RandomizeCfg.Hair | RandomizeCfg.FacialHair | RandomizeCfg.HeadTop |
            RandomizeCfg.Tail | RandomizeCfg.BodyMarkings)) != 0;

        Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>> markings;
        if (!randomizeAnyMarkings && baseAppearance != null)
        {
            markings = baseAppearance.Markings.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.ToDictionary(layer => layer.Key, layer => layer.Value.ToList()));
        }
        else
        {
            var randomized = RandomizeMarkings(species, sex, palette, protoMan, random);
            if (baseAppearance == null || (randomizeCfg & RandomizeCfg.Markings) != 0)
            {
                markings = randomized;
            }
            else
            {
                // Clone the existing selection, then replace only the requested visual layers.
                markings = baseAppearance.Markings.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.ToDictionary(layer => layer.Key, layer => layer.Value.ToList()));

                foreach (var organ in markings.Keys.Union(randomized.Keys).ToList())
                {
                    var currentLayers = markings.GetValueOrDefault(organ) ?? new();
                    var randomLayers = randomized.GetValueOrDefault(organ);
                    foreach (var layer in Enum.GetValues<HumanoidVisualLayers>())
                    {
                        if (!ShouldRandomizeLayer(layer, randomizeCfg))
                            continue;

                        if (randomLayers != null && randomLayers.TryGetValue(layer, out var selected))
                            currentLayers[layer] = selected.ToList();
                        else
                            currentLayers.Remove(layer);
                    }

                    if (currentLayers.Count > 0)
                        markings[organ] = currentLayers;
                    else
                        markings.Remove(organ);
                }
            }
        }

        HumanoidCharacterAppearance appearance = new(
            palette.EyeColor,
            palette.SkinColor,
            markings);

        // Safety step. Most systems which called Random() also called this, and not doing so caused issues with markings.
        // In the future it could *maybe* be removed, but it's probably worth the extra CPU cycles to validate this info.
        return EnsureValid(appearance, species, sex, []); // Corvax-Sponsors
    }

    private static bool ShouldRandomizeLayer(HumanoidVisualLayers layer, RandomizeCfg config)
    {
        if ((config & RandomizeCfg.Markings) != 0)
            return true;

        return layer switch
        {
            HumanoidVisualLayers.Hair => (config & RandomizeCfg.Hair) != 0,
            HumanoidVisualLayers.FacialHair => (config & RandomizeCfg.FacialHair) != 0,
            HumanoidVisualLayers.HeadTop => (config & RandomizeCfg.HeadTop) != 0,
            HumanoidVisualLayers.Tail => (config & RandomizeCfg.Tail) != 0,
            _ => (config & RandomizeCfg.BodyMarkings) != 0,
        };
    }

    public static Color ClampColor(Color color)
    {
        return new(color.RByte, color.GByte, color.BByte);
    }

    public static HumanoidCharacterAppearance EnsureValid(HumanoidCharacterAppearance appearance, ProtoId<SpeciesPrototype> species, Sex sex, string[] sponsorPrototypes) // Corvax-Sponsors
    {
        var proto = IoCManager.Resolve<IPrototypeManager>();
        var markingManager = IoCManager.Resolve<MarkingManager>();

        var skinColor = appearance.SkinColor;
        var eyeColor = ClampColor(appearance.EyeColor); // not using ClampEyeColorToStrategy so characters can have fun eye colours
        var validatedMarkings = appearance.Markings.ShallowClone();

        if (proto.TryIndex(species, out var speciesProto))
        {
            var coloration = proto.Index(speciesProto.SkinColoration);
            var organs = markingManager.GetOrgans(species);
            skinColor = coloration.Strategy.EnsureVerified(skinColor);

            // Corvax-Sponsors-Start
            foreach (var (organ, layerMarkings) in validatedMarkings)
            {
                foreach (var (_, markings) in layerMarkings)
                {
                    for (int i = markings.Count - 1; i >= 0; i--)
                    {
                        var marking = markings[i];
                        if (proto.TryIndex(marking.MarkingId, out MarkingPrototype? markingProto) &&
                            markingProto.SponsorOnly && !sponsorPrototypes.Contains(marking.MarkingId))
                        {
                            markings.RemoveAt(i);
                        }
                    }
                }
            }
            // Corvax-Sponsors-End

            foreach (var (organ, _) in appearance.Markings)
            {
                if (!organs.ContainsKey(organ))
                    validatedMarkings.Remove(organ);
            }

            foreach (var (organ, organProtoID) in organs)
            {
                if (!markingManager.TryGetMarkingData(organProtoID, out var organData))
                {
                    validatedMarkings.Remove(organ);
                    continue;
                }

                var actualMarkings = appearance.Markings.GetValueOrDefault(organ)?.ShallowClone() ?? new();

                markingManager.EnsureValidColors(actualMarkings);
                markingManager.EnsureValidGroupAndSex(actualMarkings, organData.Value.Group, sex);
                markingManager.EnsureValidLayers(actualMarkings, organData.Value.Layers);
                markingManager.EnsureValidLimits(actualMarkings, organData.Value.Group, organData.Value.Layers, skinColor, eyeColor);

                validatedMarkings[organ] = actualMarkings;
            }
        }

        return new HumanoidCharacterAppearance(
            eyeColor,
            skinColor,
            validatedMarkings);
    }

    public bool Equals(HumanoidCharacterAppearance? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return EyeColor.Equals(other.EyeColor) &&
               SkinColor.Equals(other.SkinColor) &&
               MarkingManager.MarkingsAreEqual(Markings, other.Markings);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is HumanoidCharacterAppearance other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(EyeColor, SkinColor, Markings);
    }

    public HumanoidCharacterAppearance Clone()
    {
        return new(this);
    }
}
