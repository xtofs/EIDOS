namespace Eidos.Core.Tests;

using Eidos.Core.Analysis;


public class LifecycleAnalyzerTests
{
  private static readonly IReadOnlyDictionary<string, (string Source, MachineClassification Classification, bool HasErrors)> Cases =
      new Dictionary<string, (string, MachineClassification, bool)>
      {
        ["deterministic archetype"] = ("""
                archetype Activatable composable {
                  initial: Inactive
                  states { Inactive Active }
                  transitions {
                    activate   : Inactive -> Active
                    deactivate : Active   -> Inactive
                  }
                }
                """, MachineClassification.Deterministic, false),

        ["single transition is deterministic"] = ("""
                archetype Terminable {
                  initial: Active
                  states { Active Terminated }
                  transitions {
                    terminate : Active -> Terminated
                  }
                }
                """, MachineClassification.Deterministic, false),

        ["non-deterministic distinct names"] = ("""
                archetype Terminable {
                  initial: Active
                  states { Active Terminated }
                  transitions {
                    resign  : Active -> Terminated
                    dismiss : Active -> Terminated
                  }
                }
                """, MachineClassification.NonDeterministic, false),

        ["non-deterministic via state set overlap"] = ("""
                archetype Terminable {
                  initial: Active
                  states { Active OnLeave Terminated }
                  transitions {
                    resign  : ( Active | OnLeave ) -> Terminated
                    dismiss : Active               -> Terminated
                  }
                }
                """, MachineClassification.NonDeterministic, false),

        ["ambiguous duplicate transition name"] = ("""
                archetype Terminable {
                  initial: Active
                  states { Active Terminated }
                  transitions {
                    terminate : Active -> Terminated
                    terminate : Active -> Terminated
                  }
                }
                """, MachineClassification.Ambiguous, true),

        ["ambiguous shared name via state set overlap"] = ("""
                archetype Terminable {
                  initial: Active
                  states { Active OnLeave Terminated }
                  transitions {
                    terminate : ( Active | OnLeave ) -> Terminated
                    terminate : Active               -> Terminated
                  }
                }
                """, MachineClassification.Ambiguous, true),

        ["undeclared states"] = ("""
                archetype Broken {
                  initial: Missing
                  states { Active }
                  transitions {
                    go : Active -> Nowhere
                  }
                }
                """, MachineClassification.Deterministic, true),

        ["duplicate state"] = ("""
                archetype Broken {
                  initial: Active
                  states { Active Active }
                  transitions { }
                }
                """, MachineClassification.Deterministic, true),

        ["empty archetype"] = ("""
                archetype Immutable {
                }
                """, MachineClassification.Deterministic, false),
      };

  public static TheoryData<string> CaseNames => new(Cases.Keys);

  [Theory]
  [MemberData(nameof(CaseNames))]
  public void ClassifiesMachine(string name)
  {
    var (source, classification, hasErrors) = Cases[name];

    var results = LifecycleAnalyzer.Analyze(EidosGrammarParser.Parse(source));

    var result = Assert.Single(results);
    Assert.Equal(classification, result.Classification);
    Assert.Equal(hasErrors, result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
  }

  [Fact]
  public void ResolvesArchetypeReferenceInSameDocument()
  {
    var results = LifecycleAnalyzer.Analyze(EidosGrammarParser.Parse("""
            archetype Terminable {
              initial: Active
              states { Active Terminated }
              transitions {
                resign  : Active -> Terminated
                dismiss : Active -> Terminated
              }
            }

            entity Contract {
              lifecycle: Terminable
            }
            """));

    Assert.Equal(2, results.Count);
    Assert.All(results, r => Assert.Equal(MachineClassification.NonDeterministic, r.Classification));
    Assert.Equal("Contract", results[1].OwnerName);
  }

  [Fact]
  public void UnresolvedArchetypeReferenceProducesWarning()
  {
    var results = LifecycleAnalyzer.Analyze(EidosGrammarParser.Parse("""
            entity Organization {
              lifecycle: Activatable
            }
            """));

    var result = Assert.Single(results);
    var diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    Assert.Contains("Activatable", diagnostic.Message);
  }

  [Fact]
  public void ComposedArchetypeReferenceProducesWarning()
  {
    var results = LifecycleAnalyzer.Analyze(EidosGrammarParser.Parse("""
            entity Sample {
              lifecycle: Activatable + SoftDeletable
            }
            """));

    var result = Assert.Single(results);
    var diagnostic = Assert.Single(result.Diagnostics);
    Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    Assert.Contains("composition", diagnostic.Message);
  }

  [Fact]
  public void SpecExampleSchemaAnalyzesWithoutErrors()
  {
    var results = LifecycleAnalyzer.Analyze(EidosGrammarParser.Parse("""
            entity Person {
              lifecycle {
                initial: Applicant
                states { Applicant Active Suspended Deceased }
                transitions {
                  onboard   : Applicant              -> Active
                  suspend   : Active                 -> Suspended
                  reinstate : Suspended              -> Active
                  close     : ( Active | Suspended ) -> Deceased
                }
              }
            }
            """));

    var result = Assert.Single(results);
    Assert.Equal(MachineClassification.Deterministic, result.Classification);
    Assert.Empty(result.Diagnostics);
  }
}
