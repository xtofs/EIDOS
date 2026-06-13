namespace Eidos.Core.Analysis;

/// <summary>
/// Performs the determinism analysis described in spec §5.1 on every state machine
/// in a document: inline lifecycles of entities and relationships, archetype declarations,
/// and mixin lifecycle fragments. Archetype references are resolved against
/// archetypes declared in the same document; unresolved references produce a warning
/// (cross-file imports are an open design question, spec §7.2).
/// </summary>
public sealed class LifecycleAnalyzer
{
    public static IReadOnlyList<LifecycleAnalysisResult> Analyze(EidosDocumentSyntax document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var archetypes = document.Declarations
            .OfType<ArchetypeDeclarationSyntax>()
            .GroupBy(a => a.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var results = new List<LifecycleAnalysisResult>();

        foreach (var declaration in document.Declarations)
        {
            switch (declaration)
            {
                case ArchetypeDeclarationSyntax archetype:
                    results.Add(AnalyzeMachine(archetype.Name, archetype.Lifecycle));
                    break;

                case EntityDeclarationSyntax entity:
                    AnalyzeLifecycleClauses(
                        entity.Name,
                        entity.Members.OfType<EntityLifecycleMemberSyntax>().Select(m => m.Lifecycle),
                        archetypes,
                        results);
                    break;

                case RelationshipDeclarationSyntax relationship:
                    AnalyzeLifecycleClauses(
                        relationship.Name,
                        relationship.Members.OfType<RelationshipLifecycleMemberSyntax>().Select(m => m.Lifecycle),
                        archetypes,
                        results);
                    break;

                case MixinDeclarationSyntax mixin:
                    foreach (var fragment in mixin.Members.OfType<MixinLifecycleFragmentMemberSyntax>())
                    {
                        results.Add(AnalyzeMachine(mixin.Name, fragment.Lifecycle.Lifecycle));
                    }

                    break;
            }
        }

        return results;
    }

    private static void AnalyzeLifecycleClauses(
        string ownerName,
        IEnumerable<LifecycleClauseSyntax> clauses,
        IReadOnlyDictionary<string, ArchetypeDeclarationSyntax> archetypes,
        List<LifecycleAnalysisResult> results)
    {
        foreach (var clause in clauses)
        {
            switch (clause)
            {
                case InlineLifecycleClauseSyntax inline:
                    results.Add(AnalyzeMachine(ownerName, inline.Lifecycle));
                    break;

                case ArchetypeReferenceLifecycleSyntax reference:
                    results.Add(AnalyzeArchetypeReference(ownerName, reference, archetypes));
                    break;
            }
        }
    }

    private static LifecycleAnalysisResult AnalyzeArchetypeReference(
        string ownerName,
        ArchetypeReferenceLifecycleSyntax reference,
        IReadOnlyDictionary<string, ArchetypeDeclarationSyntax> archetypes)
    {
        if (reference.Archetypes.Count > 1)
        {
            // Archetype composition (+) semantics are TBD per spec §5.3 / §7.1.
            var diagnostic = new EidosDiagnostic(
                DiagnosticSeverity.Warning,
                $"'{ownerName}': archetype composition '{string.Join(" + ", reference.Archetypes)}' is not analyzed (spec §5.3 TBD).",
                reference.Span);
            return new LifecycleAnalysisResult(ownerName, MachineClassification.Deterministic, [diagnostic]);
        }

        var name = reference.Archetypes[0];
        if (!archetypes.TryGetValue(name, out var archetype))
        {
            var diagnostic = new EidosDiagnostic(
                DiagnosticSeverity.Warning,
                $"'{ownerName}': archetype '{name}' is not declared in this document; lifecycle not analyzed.",
                reference.Span);
            return new LifecycleAnalysisResult(ownerName, MachineClassification.Deterministic, [diagnostic]);
        }

        var result = AnalyzeMachine(ownerName, archetype.Lifecycle);
        return result;
    }

    private static LifecycleAnalysisResult AnalyzeMachine(string ownerName, InlineLifecycleSyntax lifecycle)
    {
        var diagnostics = new List<EidosDiagnostic>();

        var states = lifecycle.Members
            .OfType<StatesBlockSyntax>()
            .SelectMany(b => b.States)
            .ToList();
        var stateNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            if (!stateNames.Add(state.Name))
            {
                diagnostics.Add(new EidosDiagnostic(
                    DiagnosticSeverity.Error,
                    $"'{ownerName}': state '{state.Name}' is declared more than once.",
                    state.Span));
            }
        }

        foreach (var initial in lifecycle.Members.OfType<InitialClauseSyntax>())
        {
            if (states.Count > 0 && !stateNames.Contains(initial.InitialState))
            {
                diagnostics.Add(new EidosDiagnostic(
                    DiagnosticSeverity.Error,
                    $"'{ownerName}': initial state '{initial.InitialState}' is not a declared state.",
                    initial.Span));
            }
        }

        var transitions = lifecycle.Members
            .OfType<TransitionsBlockSyntax>()
            .SelectMany(b => b.Transitions)
            .ToList();

        // Expand state sets into individual (source, target) edges.
        var edges = transitions
            .SelectMany(t => SourceStatesOf(t).Select(source => (Source: source, Transition: t)))
            .ToList();

        if (states.Count > 0)
        {
            foreach (var (source, transition) in edges)
            {
                if (!stateNames.Contains(source))
                {
                    diagnostics.Add(new EidosDiagnostic(
                        DiagnosticSeverity.Error,
                        $"'{ownerName}': transition '{transition.Name}' references undeclared source state '{source}'.",
                        transition.Span));
                }
            }

            foreach (var transition in transitions)
            {
                if (!stateNames.Contains(transition.TargetState))
                {
                    diagnostics.Add(new EidosDiagnostic(
                        DiagnosticSeverity.Error,
                        $"'{ownerName}': transition '{transition.Name}' references undeclared target state '{transition.TargetState}'.",
                        transition.Span));
                }
            }
        }

        var classification = ClassifyEdges(ownerName, edges, diagnostics);
        return new LifecycleAnalysisResult(ownerName, classification, diagnostics);
    }

    private static MachineClassification ClassifyEdges(
        string ownerName,
        IReadOnlyList<(string Source, TransitionDeclarationSyntax Transition)> edges,
        List<EidosDiagnostic> diagnostics)
    {
        var classification = MachineClassification.Deterministic;

        // Each (source, target) pair reached by more than one transition is a non-deterministic
        // choice. The transition name is the discriminator: distinct names disambiguate it
        // (non-deterministic, resolvable via the 'transition' field), repeated names cannot
        // (ambiguous, an error). No separate discriminator declaration is involved — see spec §5.1.
        var conflictGroups = edges
            .GroupBy(e => (e.Source, e.Transition.TargetState))
            .Where(g => g.Count() > 1);

        foreach (var group in conflictGroups)
        {
            var conflicting = group.Select(e => e.Transition).ToList();

            var repeatedNames = conflicting
                .GroupBy(t => t.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (repeatedNames.Count > 0)
            {
                diagnostics.Add(new EidosDiagnostic(
                    DiagnosticSeverity.Error,
                    $"'{ownerName}': source '{group.Key.Source}' and target '{group.Key.TargetState}' are reached by multiple transitions sharing the name {FormatQuoted(repeatedNames)}; the transition name cannot disambiguate them. Rename the conflicting transitions.",
                    conflicting[0].Span));
                classification = MachineClassification.Ambiguous;
                continue;
            }

            diagnostics.Add(new EidosDiagnostic(
                DiagnosticSeverity.Warning,
                $"'{ownerName}': transitions {FormatNames(conflicting)} share source '{group.Key.Source}' and target '{group.Key.TargetState}'; 'PUT /_state' requires a 'transition' field to reach '{group.Key.TargetState}'. Consider distinct (source, target) pairs to keep the machine deterministic.",
                conflicting[0].Span));

            if (classification == MachineClassification.Deterministic)
            {
                classification = MachineClassification.NonDeterministic;
            }
        }

        return classification;
    }

    private static string FormatQuoted(IEnumerable<string> names)
    {
        return string.Join(", ", names.Select(n => $"'{n}'"));
    }

    private static IEnumerable<string> SourceStatesOf(TransitionDeclarationSyntax transition)
    {
        return transition.SourceStates switch
        {
            SingleStateSetSyntax single => [single.StateName],
            MultiStateSetSyntax multi => multi.StateNames,
            _ => []
        };
    }

    private static string FormatNames(IEnumerable<TransitionDeclarationSyntax> transitions)
    {
        return string.Join(", ", transitions.Select(t => $"'{t.Name}'"));
    }
}
