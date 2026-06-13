namespace Eidos.Core.Analysis;

/// <summary>
/// Classification of a state machine per spec §5.1 Determinism Analysis.
/// </summary>
public enum MachineClassification
{
    /// <summary>Every (source, target) pair is reached by exactly one transition.</summary>
    Deterministic,

    /// <summary>
    /// One or more (source, target) pairs are reached by several transitions with distinct
    /// names. The transition name disambiguates them, so <c>PUT /_state</c> requires a
    /// <c>transition</c> field for the affected targets.
    /// </summary>
    NonDeterministic,

    /// <summary>
    /// One or more (source, target) pairs are reached by several transitions sharing the same
    /// name, so the transition name cannot disambiguate them (error).
    /// </summary>
    Ambiguous
}

public enum DiagnosticSeverity
{
    Warning,
    Error
}

public sealed record EidosDiagnostic(DiagnosticSeverity Severity, string Message, SourceSpan Span);

/// <summary>
/// Result of analyzing a single lifecycle (state machine), identified by the
/// declaration that owns it.
/// </summary>
public sealed record LifecycleAnalysisResult(
    string OwnerName,
    MachineClassification Classification,
    IReadOnlyList<EidosDiagnostic> Diagnostics);
