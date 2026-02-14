using System.Linq;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Offbrand.Wounds;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class WoundableHealthAnalyzerData
{
    [DataField]
    public float BrainHealth;

    [DataField]
    public AttributeRating BrainHealthRating; // VDS

    [DataField]
    public float HeartHealth;

    [DataField]
    public AttributeRating HeartHealthRating; // VDS

    [DataField]
    public (int, int) BloodPressure;

    [DataField]
    public AttributeRating BloodFlowRating; // VDS

    [DataField]
    public int HeartRate;

    [DataField]
    public AttributeRating HeartRateRating; // VDS

    [DataField]
    public float HeartStrain;

    [DataField]
    public int Etco2;

    [DataField]
    public int RespiratoryRate;

    [DataField]
    public float RespiratoryRateModifier;

    [DataField]
    public float Spo2;

    [DataField]
    public AttributeRating BloodOxygenationRating; // VDS

    [DataField]
    public float LungHealth;

    [DataField]
    public AttributeRating LungHealthRating; // VDS

    [DataField]
    public AttributeRating DamageRating; // VDS

    [DataField]
    public bool AnyVitalCritical;

    [DataField]
    public LocId Etco2Name;

    [DataField]
    public LocId Etco2GasName;

    [DataField]
    public LocId Spo2Name;

    [DataField]
    public LocId Spo2GasName;

    [DataField]
    public List<string>? Wounds;

    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, (FixedPoint2 InBloodstream, FixedPoint2 Metabolites)>? Reagents;

    [DataField]
    public bool NonMedicalReagents;

    [DataField]
    public MetricRanking Ranking;
}

[Serializable, NetSerializable]
public enum MetricRanking : byte
{
    Good = 0,
    Okay = 1,
    Poor = 2,
    Bad = 3,
    Dangerous = 4,
}

[Serializable, NetSerializable]
public enum AttributeRating : byte // VDS - reverted rating removal
{
    Good = 0,
    Okay = 1,
    Poor = 2,
    Bad = 3,
    Awful = 4,
    Dangerous = 5,
}

public abstract class SharedWoundableHealthAnalyzerSystem : EntitySystem
{
    [Dependency] private readonly BrainDamageSystem _brainDamage = default!;
    [Dependency] private readonly HeartSystem _heart = default!;
    [Dependency] private readonly ShockThresholdsSystem _shockThresholds = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;

    protected const string MedicineGroup = "Medicine";

    private AttributeRating RateHigherIsBetter(double value) // VDS
    {
        return RateHigherIsWorse(1d - value);
    }

    private AttributeRating RateHigherIsWorse(double value) // VDS
    {
        return (AttributeRating)(byte)Math.Clamp(Math.Floor(6d * value), 0d, 5d);
    }
    public List<string>? SampleWounds(EntityUid uid)
    {
        if (!_statusEffects.TryEffectsWithComp<AnalyzableWoundComponent>(uid, out var wounds))
            return null;

        var described = new List<string>();

        foreach (var analyzable in wounds)
        {
            var wound = Comp<WoundComponent>(analyzable);
            var damage = wound.Damage;

            if (analyzable.Comp1.Descriptions.HighestMatch(damage.GetTotal()) is { } message)
                described.Add(message);
        }

        return described;
    }

    public virtual Dictionary<ProtoId<ReagentPrototype>, (FixedPoint2 InBloodstream, FixedPoint2 Metabolites)>? SampleReagents(EntityUid uid, out bool hasNonMedical)
    {
        hasNonMedical = false;
        return null;
    }

    public MetricRanking Ranking(Entity<HeartrateComponent> ent)
    {
        var strain = (MetricRanking)Math.Min((int)MathF.Round(4f * _heart.Strain(ent)), 4);
        var spo2 = (MetricRanking)Math.Min((int)MathF.Round(4f * (1f - _heart.Spo2(ent).Float())), 4);

        if ((byte)spo2 > (byte)strain)
            return spo2;

        return strain;
    }

    public WoundableHealthAnalyzerData? TakeSample(EntityUid uid, bool withWounds = true)
    {
        if (!HasComp<WoundableComponent>(uid))
            return null;

        if (!TryComp<HeartrateComponent>(uid, out var heartrate))
            return null;

        if (!TryComp<BrainDamageComponent>(uid, out var brainDamage))
            return null;

        if (!TryComp<LungDamageComponent>(uid, out var lungDamage))
            return null;

        if (!TryComp<ShockThresholdsComponent>(uid, out var shockThresholds)) // VDS
            return null;

        if (!TryComp<DamageableComponent>(uid, out var damageable)) // VDS
            return null;

        var brainHealth = 1f - ((float)brainDamage.Damage / (float)brainDamage.MaxDamage);
        var heartHealth = 1f - ((float)heartrate.Damage / (float)heartrate.MaxDamage);
        var lungHealth = 1f - ((float)lungDamage.Damage / (float)lungDamage.MaxDamage);
        var totalDamage = 1f - ((float)damageable.TotalDamage / (shockThresholds.MobThresholds.Max(x => (float?)x.Key) ?? 200f));
        var (upper, lower) = _heart.BloodPressure((uid, heartrate));
        var oxygenation = (float)_heart.Spo2((uid, heartrate)).Double(); // VDS
        var flow = (float)_heart.ComputeExhaleEfficiencyModifier((uid, heartrate)); // VDS

        var hasNonMedical = false;
        var reagents = withWounds ? SampleReagents(uid, out hasNonMedical) : null;

        return new WoundableHealthAnalyzerData()
            {
                BrainHealth = brainHealth,
                BrainHealthRating = RateHigherIsBetter(brainHealth), // VDS
                HeartHealth = heartHealth,
                HeartHealthRating = RateHigherIsBetter(heartHealth), // VDS
                BloodPressure = (upper, lower),
                HeartRate = _heart.HeartRate((uid, heartrate)),
                HeartRateRating = !heartrate.Running ? AttributeRating.Dangerous : RateHigherIsWorse(_heart.Strain((uid, heartrate))), // VDS
                HeartStrain = _heart.Strain((uid, heartrate)),
                Etco2 = _heart.Etco2((uid, heartrate)),
                BloodFlowRating = RateHigherIsBetter(flow), // VDS
                RespiratoryRate = _heart.RespiratoryRate((uid, heartrate)),
                RespiratoryRateModifier = _heart.ComputeRespiratoryRateModifier((uid, heartrate)),
                Spo2 = _heart.Spo2((uid, heartrate)).Float(),
                BloodOxygenationRating = RateHigherIsBetter(oxygenation), // VDS
                LungHealth = lungHealth,
                LungHealthRating = RateHigherIsBetter(lungHealth), // VDS
                AnyVitalCritical = _shockThresholds.IsCritical(uid) || _brainDamage.IsCritical(uid) || _heart.IsCritical(uid),
                Etco2Name = heartrate.Etco2Name,
                Etco2GasName = heartrate.Etco2GasName,
                Spo2Name = heartrate.Spo2Name,
                Spo2GasName = heartrate.Spo2GasName,
                DamageRating = RateHigherIsBetter(totalDamage),
                Wounds = withWounds ? SampleWounds(uid) : null,
                Reagents = reagents,
                NonMedicalReagents = hasNonMedical,
                Ranking = Ranking((uid, heartrate)),
            };
    }
}
