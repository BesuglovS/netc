namespace NetcShared;

/// <summary>Набор целевых ПК: полный список, отображаемые имена и первоначально выбранные.</summary>
public sealed record TargetSet(
    IReadOnlyList<string> All,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> Initial);
