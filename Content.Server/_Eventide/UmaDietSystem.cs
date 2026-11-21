using System.Linq;
using Content.Shared._Eventide;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Nutrition;
using Content.Shared.FixedPoint;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._Eventide;

/// <summary>
/// Applies a gameplay food intolerance effect to Uma characters after they actually consume
/// foods that are unsafe for their equine digestive profile.
/// </summary>
public sealed class UmaDietSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TagSystem _tags = default!;

    private static readonly ProtoId<TagPrototype> MeatTag = "Meat";
    private static readonly ProtoId<TagPrototype> CheeseTag = "Cheese";
    private static readonly ProtoId<TagPrototype> BreadTag = "Bread";
    private static readonly ProtoId<TagPrototype> CakeTag = "Cake";

    private static readonly HashSet<string> ToxicFlavors = new(StringComparer.OrdinalIgnoreCase)
    {
        "chocolate", "cabbage", "potatoes", "tomato", "eggplant",
        "avocado", "onion", "garlic", "peas", "beans", "bean",
        "bread", "dough", "sweetdough", "cake", "cookie"
    };

    private static readonly HashSet<string> ToxicReagents = new(StringComparer.OrdinalIgnoreCase)
    {
        "CocoaPowder", "Theobromine", "Allicin", "Milk", "Butter"
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EdibleComponent, IngestedEvent>(OnFoodIngested);
    }

    private void OnFoodIngested(Entity<EdibleComponent> food, ref IngestedEvent args)
    {
        if (!HasComp<UmaDietComponent>(args.Target) || !IsUnsafeFood(food.Owner, args.Split))
            return;

        // A moderate toxin hit makes forbidden foods matter without instantly incapacitating a character.
        var poison = new DamageSpecifier(ProtoMan.Index<DamageTypePrototype>("Poison"), FixedPoint2.New(6));
        _damageable.TryChangeDamage(args.Target, poison, origin: food.Owner);
        _popup.PopupEntity(Loc.GetString("uma-diet-poisoned"), args.Target, args.Target);
    }

    private bool IsUnsafeFood(EntityUid food, Solution split)
    {
        if (_tags.HasTag(food, MeatTag) ||
            _tags.HasTag(food, CheeseTag) ||
            _tags.HasTag(food, BreadTag) ||
            _tags.HasTag(food, CakeTag))
            return true;

        if (TryComp<FlavorProfileComponent>(food, out var flavor) &&
            flavor.Flavors.Any(entry => ToxicFlavors.Contains(entry.Id)))
            return true;

        return split.Contents.Any(entry => ToxicReagents.Contains(entry.Reagent.Prototype.Id));
    }
}
