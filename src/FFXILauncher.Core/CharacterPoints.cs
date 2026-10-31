namespace FFXILauncher.Core;

public sealed record CharacterPoints(int? Experience, int? ExperienceCap, int? Merits, int? MeritCap, int? Capacity, int? JobPoints, int? Exemplar = null, int? ExemplarRequired = null)
{
    public const int CapacityMaximum = 29999;
    public const int CapacityConversion = 30000;
    public const int JobPointMaximum = 500;
    public string Describe(int? masterLevel = null, bool showMasterProgress = true)
    {
        static string Value(int? n) => n?.ToString("N0") ?? "?";
        var text = $"{Value(Experience)}/{Value(ExperienceCap)} EXP · {Value(Merits)}/{Value(MeritCap)} Merits\n" +
            $"{Value(Capacity)}/{CapacityConversion:N0} Capacity → JP · {Value(JobPoints)}/{JobPointMaximum:N0} Job Points";
        if (showMasterProgress)
            text += masterLevel == 50 ? "\nMaster level 50 · capped" :
                $"\n{Value(Exemplar)}/{Value(ExemplarRequired is > 0 ? ExemplarRequired : null)} Exemplar Points → ML {(masterLevel is {} ml ? (ml + 1).ToString() : "?")}";
        return text;
    }
}
