namespace Eidos.Core.Tests;

public class SpecExampleTests
{
  private static readonly IReadOnlyDictionary<string, (string Source, int DeclarationCount)> Examples =
      new Dictionary<string, (string, int)>
      {
        ["§6.1 hr-archetypes.eidos"] = (HrArchetypes, 1),
        ["§6.2 hr.eidos"] = (HrSchema, 4),
        ["A.1 Immutable"] = ("""
                archetype Immutable {
                  // No states, no transitions.
                  // Compiler generates POST only. No PATCH, no DELETE.
                }
                """, 1),
        ["A.2 SoftDeletable"] = ("""
                archetype SoftDeletable composable {
                  initial: Active

                  states {
                    Active
                    Deleted
                  }

                  transitions {
                    delete  : Active  -> Deleted
                    restore : Deleted -> Active
                  }
                }
                """, 1),
        ["A.3 Publishable"] = ("""
                archetype Publishable {
                  initial: Draft

                  states {
                    Draft
                    UnderReview
                    Published
                    Archived
                  }

                  transitions {
                    submit    : Draft       -> UnderReview
                    publish   : UnderReview -> Published
                    reject    : UnderReview -> Draft
                    archive   : Published   -> Archived
                    unarchive : Archived    -> Published
                  }
                }
                """, 1),
        ["A.4 Approvable"] = ("""
                archetype Approvable {
                  initial: Draft

                  states {
                    Draft
                    UnderReview
                    Approved
                    Rejected
                  }

                  transitions {
                    submit  : Draft       -> UnderReview
                    approve : UnderReview -> Approved
                    reject  : UnderReview -> Rejected
                    revise  : Rejected    -> Draft
                  }
                }
                """, 1),
        ["A.5 Activatable"] = ("""
                archetype Activatable composable {
                  initial: Inactive

                  states {
                    Inactive
                    Active
                  }

                  transitions {
                    activate   : Inactive -> Active
                    deactivate : Active   -> Inactive
                  }
                }
                """, 1),
        ["A.6 Disableable"] = ("""
                archetype Disableable composable {
                  initial: Enabled

                  states {
                    Enabled
                    Disabled
                  }

                  transitions {
                    disable : Enabled  -> Disabled
                    enable  : Disabled -> Enabled
                  }
                }
                """, 1),
        ["A.7 WriteOnce"] = ("""
                archetype WriteOnce {
                  // No states, no transitions.
                }
                """, 1),
        ["A.8 ReadOnly"] = ("""
                archetype ReadOnly {
                  // No states, no transitions.
                }
                """, 1),
        ["§5.1 non-deterministic transitions"] = ("""
                entity Contract {
                  lifecycle {
                    initial: Active

                    states {
                      Active
                      Terminated
                    }

                    transitions {
                      resign  : Active -> Terminated
                      dismiss : Active -> Terminated
                    }
                  }
                }
                """, 1),
      };

  public static TheoryData<string> ExampleNames =>
      new(Examples.Keys);

  [Theory]
  [MemberData(nameof(ExampleNames))]
  public void SpecExampleParses(string name)
  {
    var (source, declarationCount) = Examples[name];

    var document = EidosGrammarParser.Parse(source);

    Assert.Equal(declarationCount, document.Declarations.Count);
  }

  [Fact]
  public void EmployableArchetypeHasExpectedStructure()
  {
    var document = EidosGrammarParser.Parse(HrArchetypes);

    var archetype = Assert.IsType<ArchetypeDeclarationSyntax>(document.Declarations.Single());
    Assert.Equal("Employable", archetype.Name);
    Assert.True(archetype.IsComposable);

    var initial = archetype.Lifecycle.Members.OfType<InitialClauseSyntax>().Single();
    Assert.Equal("Probationary", initial.InitialState);

    var states = archetype.Lifecycle.Members.OfType<StatesBlockSyntax>().Single().States;
    Assert.Equal(["Probationary", "Active", "OnLeave", "Terminated"], states.Select(s => s.Name));
    Assert.Equal("On trial period", states[0].Label);

    var transitions = archetype.Lifecycle.Members.OfType<TransitionsBlockSyntax>().Single().Transitions;
    Assert.Equal(["confirm", "grantLeave", "return", "terminate"], transitions.Select(t => t.Name));

    var terminate = transitions[3];
    var sources = Assert.IsType<MultiStateSetSyntax>(terminate.SourceStates);
    Assert.Equal(["Active", "OnLeave", "Probationary"], sources.StateNames);
    Assert.Equal("Terminated", terminate.TargetState);
  }

  [Fact]
  public void HrSchemaHasExpectedStructure()
  {
    var document = EidosGrammarParser.Parse(HrSchema);

    var mixin = Assert.IsType<MixinDeclarationSyntax>(document.Declarations[0]);
    Assert.Equal("Auditable", mixin.Name);
    var mixinProperties = mixin.Members.OfType<MixinPropertiesMemberSyntax>().Single().Properties.Properties;
    Assert.Equal(3, mixinProperties.Count);
    Assert.IsType<ReferenceTypeExpressionSyntax>(mixinProperties[2].Type);

    var person = Assert.IsType<EntityDeclarationSyntax>(document.Declarations[1]);
    Assert.Equal("Person", person.Name);
    Assert.Equal(["Auditable"], person.Mixins);
    var personDoc = person.Members.OfType<EntityDocCommentMemberSyntax>().Single().Comment;
    Assert.Equal("A natural person known to the system.", personDoc.Text);

    var organization = Assert.IsType<EntityDeclarationSyntax>(document.Declarations[2]);
    var organizationLifecycle = organization.Members.OfType<EntityLifecycleMemberSyntax>().Single().Lifecycle;
    var archetypeReference = Assert.IsType<ArchetypeReferenceLifecycleSyntax>(organizationLifecycle);
    Assert.Equal(["Activatable"], archetypeReference.Archetypes);

    var employment = Assert.IsType<RelationshipDeclarationSyntax>(document.Declarations[3]);
    Assert.Equal("Employment", employment.Name);
    Assert.Equal(2, employment.Participants.Count);
    var employee = employment.Participants[0];
    Assert.Equal("employee", employee.Name);
    Assert.Equal("Person", employee.TypeName);
    var roleOption = Assert.IsType<ParticipantRoleOptionSyntax>(employee.Options.Single());
    Assert.Equal("Employee", roleOption.RoleTypeName);

    var coupling = employment.Members.OfType<RelationshipCouplingMemberSyntax>().Single().Coupling;
    Assert.Equal(2, coupling.Rules.Count);
    Assert.Equal("employer", coupling.Rules[0].ParticipantName);
    Assert.Equal("Dissolved", coupling.Rules[0].TargetState);
    Assert.Equal("terminate", coupling.Rules[0].Call.Name);

    var role = employment.Members.OfType<RelationshipRoleMemberSyntax>().Single().Role;
    Assert.Equal("Employee", role.Name);
    var roleProperties = role.Members.OfType<RolePropertiesMemberSyntax>().Single().Properties.Properties;
    Assert.Equal(["employeeId", "department"], roleProperties.Select(p => p.Name));
  }

  private const string HrArchetypes = """
        archetype Employable composable {
          initial: Probationary

          states {
            Probationary  "On trial period"
            Active        "Fully employed"
            OnLeave       "Approved leave of absence"
            Terminated    "Employment ended"
          }

          transitions {
            confirm    : Probationary -> Active
            grantLeave : Active       -> OnLeave
            return     : OnLeave      -> Active
            terminate  : ( Active | OnLeave | Probationary ) -> Terminated
          }
        }
        """;

  private const string HrSchema = """"
        mixin Auditable {
          properties {
            createdAt  : DateTime  [ readonly ]
            modifiedAt : DateTime  [ readonly ]
            createdBy  : ref Person [ readonly ]
          }
        }

        entity Person with Auditable {
          """A natural person known to the system."""

          properties {
            name   : String  [ required ]
            email  : Email   [ required, unique ]
            taxId  : String  [ writeonce ]
          }

          lifecycle {
            initial: Applicant

            states {
              Applicant
              Active
              Suspended
              Deceased
            }

            transitions {
              onboard   : Applicant              -> Active     [ guard: taxIdVerified ]
              suspend   : Active                 -> Suspended
              reinstate : Suspended              -> Active
              close     : ( Active | Suspended ) -> Deceased
            }
          }
        }

        entity Organization with Auditable {
          properties {
            legalName : String [ required ]
            vatNumber : String
          }

          lifecycle: Activatable
        }

        relationship Employment between
          employee : Person       [ role: Employee ],
          employer : Organization [ role: Employer ] {

          """Mediates the employment relationship between a Person and an Organization."""

          properties {
            startDate : Date   [ required ]
            endDate   : Date   [ nullable ]
            title     : String
            salary    : Money
          }

          lifecycle: Employable

          coupling {
            on employer.state -> Dissolved : terminate
            on employee.state -> Deceased  : terminate
          }

          role Employee {
            properties {
              employeeId  : String [ required ]
              department  : String
            }
          }
        }
        """";
}

public class GuardExpressionTests
{
  public static TheoryData<string, Type> Guards => new()
    {
        { "verified", typeof(GuardPredicateNameSyntax) },
        { "a && b", typeof(GuardBinaryExpressionSyntax) },
        { "a || b", typeof(GuardBinaryExpressionSyntax) },
        { "!a", typeof(GuardNotExpressionSyntax) },
        { "(a && b)", typeof(GuardParenthesizedExpressionSyntax) },
        { "employee.state in [Active, OnLeave]", typeof(ParticipantStateGuardSyntax) },
        { "a && b || c", typeof(GuardBinaryExpressionSyntax) },
        { "!(a || b) && c", typeof(GuardBinaryExpressionSyntax) },
    };

  [Theory]
  [MemberData(nameof(Guards))]
  public void GuardParsesToExpectedRoot(string guardText, Type expectedRootType)
  {
    var guard = ParseGuard(guardText);

    Assert.IsType(expectedRootType, guard);
  }

  [Fact]
  public void AndBindsTighterThanOr()
  {
    var guard = ParseGuard("a || b && c");

    var or = Assert.IsType<GuardBinaryExpressionSyntax>(guard);
    Assert.Equal(GuardBinaryOperator.Or, or.Operator);
    var right = Assert.IsType<GuardBinaryExpressionSyntax>(or.Right);
    Assert.Equal(GuardBinaryOperator.And, right.Operator);
  }

  [Fact]
  public void RelataStateGuardCapturesParticipantAndStates()
  {
    var guard = ParseGuard("employee.state in [Active, OnLeave]");

    var participants = Assert.IsType<ParticipantStateGuardSyntax>(guard);
    Assert.Equal("employee", participants.ParticipantName);
    Assert.Equal(["Active", "OnLeave"], participants.AllowedStates);
  }

  private static GuardExpressionSyntax ParseGuard(string guardText)
  {
    var source = $$"""
            entity Sample {
              lifecycle {
                initial: A
                states { A B }
                transitions {
                  go : A -> B [ guard: {{guardText}} ]
                }
              }
            }
            """;

    var document = EidosGrammarParser.Parse(source);
    var entity = Assert.IsType<EntityDeclarationSyntax>(document.Declarations.Single());
    var lifecycle = Assert.IsType<InlineLifecycleClauseSyntax>(
        entity.Members.OfType<EntityLifecycleMemberSyntax>().Single().Lifecycle);
    var transition = lifecycle.Lifecycle.Members.OfType<TransitionsBlockSyntax>().Single().Transitions.Single();
    return Assert.IsType<GuardTransitionOptionSyntax>(transition.Options.Single()).Guard;
  }
}

public class TypeExpressionTests
{
  public static TheoryData<string, Type> Types => new()
    {
        { "String", typeof(ScalarTypeExpressionSyntax) },
        { "Integer", typeof(ScalarTypeExpressionSyntax) },
        { "Number", typeof(ScalarTypeExpressionSyntax) },
        { "Boolean", typeof(ScalarTypeExpressionSyntax) },
        { "Date", typeof(ScalarTypeExpressionSyntax) },
        { "DateTime", typeof(ScalarTypeExpressionSyntax) },
        { "Time", typeof(ScalarTypeExpressionSyntax) },
        { "Money", typeof(ScalarTypeExpressionSyntax) },
        { "Email", typeof(ScalarTypeExpressionSyntax) },
        { "Url", typeof(ScalarTypeExpressionSyntax) },
        { "Uuid", typeof(ScalarTypeExpressionSyntax) },
        { "ref Person", typeof(ReferenceTypeExpressionSyntax) },
        { "list<String>", typeof(ListTypeExpressionSyntax) },
        { "optional<Date>", typeof(OptionalTypeExpressionSyntax) },
        { "list<optional<ref Person>>", typeof(ListTypeExpressionSyntax) },
        { "enum [ small, large ]", typeof(EnumTypeExpressionSyntax) },
    };

  [Theory]
  [MemberData(nameof(Types))]
  public void TypeExpressionParses(string typeText, Type expectedType)
  {
    var type = ParseType(typeText);

    Assert.IsType(expectedType, type);
  }

  [Fact]
  public void EnumValuesAllowHyphenatedNames()
  {
    var type = Assert.IsType<EnumTypeExpressionSyntax>(ParseType("enum [ resignation, mutual-agreement ]"));

    Assert.Equal(["resignation", "mutual-agreement"], type.Values);
  }

  public static TheoryData<string, PropertyFlagKind> Flags => new()
    {
        { "required", PropertyFlagKind.Required },
        { "unique", PropertyFlagKind.Unique },
        { "readonly", PropertyFlagKind.ReadOnly },
        { "writeonce", PropertyFlagKind.WriteOnce },
        { "immutable", PropertyFlagKind.Immutable },
        { "nullable", PropertyFlagKind.Nullable },
    };

  [Theory]
  [MemberData(nameof(Flags))]
  public void PropertyFlagParses(string flagText, PropertyFlagKind expected)
  {
    var document = EidosGrammarParser.Parse($$"""
            entity Sample {
              properties {
                value : String [ {{flagText}} ]
              }
            }
            """);

    var entity = Assert.IsType<EntityDeclarationSyntax>(document.Declarations.Single());
    var property = entity.Members.OfType<EntityPropertiesMemberSyntax>().Single().Properties.Properties.Single();
    var flag = Assert.IsType<PropertyFlagOptionSyntax>(property.Options.Single());
    Assert.Equal(expected, flag.Flag);
  }

  [Fact]
  public void StateScopedOptionsParse()
  {
    var document = EidosGrammarParser.Parse("""
            entity Sample {
              properties {
                value : String [ writable-in: [ Draft ], visible-in: [ Draft, Published ] ]
              }
            }
            """);

    var entity = Assert.IsType<EntityDeclarationSyntax>(document.Declarations.Single());
    var property = entity.Members.OfType<EntityPropertiesMemberSyntax>().Single().Properties.Properties.Single();
    var writable = Assert.IsType<WritableInOptionSyntax>(property.Options[0]);
    Assert.Equal(["Draft"], writable.StateNames);
    var visible = Assert.IsType<VisibleInOptionSyntax>(property.Options[1]);
    Assert.Equal(["Draft", "Published"], visible.StateNames);
  }

  private static TypeExpressionSyntax ParseType(string typeText)
  {
    var document = EidosGrammarParser.Parse($$"""
            entity Sample {
              properties {
                value : {{typeText}}
              }
            }
            """);

    var entity = Assert.IsType<EntityDeclarationSyntax>(document.Declarations.Single());
    return entity.Members.OfType<EntityPropertiesMemberSyntax>().Single().Properties.Properties.Single().Type;
  }
}

public class ParticipantTests
{
  public static TheoryData<string, MultiplicityKind> Multiplicities => new()
    {
        { "one", MultiplicityKind.One },
        { "many", MultiplicityKind.Many },
        { "one-or-more", MultiplicityKind.OneOrMore },
        { "zero-or-one", MultiplicityKind.ZeroOrOne },
    };

  [Theory]
  [MemberData(nameof(Multiplicities))]
  public void MultiplicityOptionParses(string multiplicityText, MultiplicityKind expected)
  {
    var document = EidosGrammarParser.Parse($$"""
            relationship Sample between
              a : Foo [ multiplicity: {{multiplicityText}} ],
              b : Bar {
            }
            """);

    var relationship = Assert.IsType<RelationshipDeclarationSyntax>(document.Declarations.Single());
    var option = Assert.IsType<ParticipantMultiplicityOptionSyntax>(relationship.Participants[0].Options.Single());
    Assert.Equal(expected, option.Multiplicity);
  }
}

public class AnnotationTests
{
  [Fact]
  public void AnnotationWithoutArgumentsParses()
  {
    var document = EidosGrammarParser.Parse("""
            @deprecated
            entity Sample {
            }
            """);

    var annotation = document.Declarations.Single().Annotations.Single();
    Assert.Equal("deprecated", annotation.Name);
    Assert.Empty(annotation.Arguments);
  }

  [Fact]
  public void AnnotationArgumentsCaptureValueKinds()
  {
    var document = EidosGrammarParser.Parse("""
            @meta(label: "Sample", owner: hrTeam, version: 3)
            entity Sample {
            }
            """);

    var annotation = document.Declarations.Single().Annotations.Single();
    Assert.Equal(3, annotation.Arguments.Count);
    Assert.Equal("Sample", Assert.IsType<AnnotationStringValueSyntax>(annotation.Arguments[0].Value).Value);
    Assert.Equal("hrTeam", Assert.IsType<AnnotationNameValueSyntax>(annotation.Arguments[1].Value).Value);
    Assert.Equal(3, Assert.IsType<AnnotationIntegerValueSyntax>(annotation.Arguments[2].Value).Value);
  }
}

public class MiscellaneousSyntaxTests
{
  [Fact]
  public void UrlHintParses()
  {
    var document = EidosGrammarParser.Parse("""
            entity Person {
              url: "people"
            }
            """);

    var entity = Assert.IsType<EntityDeclarationSyntax>(document.Declarations.Single());
    var urlHint = entity.Members.OfType<EntityUrlHintMemberSyntax>().Single().UrlHint;
    Assert.Equal("people", urlHint.Value);
  }

  [Fact]
  public void ComposedArchetypeReferenceParses()
  {
    var document = EidosGrammarParser.Parse("""
            entity Sample {
              lifecycle: Activatable + SoftDeletable
            }
            """);

    var entity = Assert.IsType<EntityDeclarationSyntax>(document.Declarations.Single());
    var lifecycle = Assert.IsType<ArchetypeReferenceLifecycleSyntax>(
        entity.Members.OfType<EntityLifecycleMemberSyntax>().Single().Lifecycle);
    Assert.Equal(["Activatable", "SoftDeletable"], lifecycle.Archetypes);
  }

  [Fact]
  public void CouplingArgumentsParse()
  {
    var document = EidosGrammarParser.Parse("""
            relationship Sample between
              a : Foo,
              b : Bar {

              coupling {
                on a.state -> Gone : terminate( reason: natural-end, note: "closed" )
              }
            }
            """);

    var relationship = Assert.IsType<RelationshipDeclarationSyntax>(document.Declarations.Single());
    var rule = relationship.Members.OfType<RelationshipCouplingMemberSyntax>().Single().Coupling.Rules.Single();
    Assert.Equal(2, rule.Call.Arguments.Count);
    Assert.Equal("natural-end", Assert.IsType<CouplingNameValueSyntax>(rule.Call.Arguments[0].Value).Name);
    Assert.Equal("closed", Assert.IsType<CouplingStringValueSyntax>(rule.Call.Arguments[1].Value).Value);
  }

  [Fact]
  public void RoleGuardConstraintParses()
  {
    var document = EidosGrammarParser.Parse("""
            relationship Sample between
              a : Foo,
              b : Bar {

              role Carrier {
                requires: isAdult && !isBlocked
              }
            }
            """);

    var relationship = Assert.IsType<RelationshipDeclarationSyntax>(document.Declarations.Single());
    var role = relationship.Members.OfType<RelationshipRoleMemberSyntax>().Single().Role;
    var constraint = Assert.IsType<RoleGuardConstraintSyntax>(role.Members.Single());
    Assert.IsType<GuardBinaryExpressionSyntax>(constraint.Guard);
  }

  [Fact]
  public void PositionTrackingReportsLineAndColumn()
  {
    var document = EidosGrammarParser.Parse("\n\n  entity Person {\n  }\n");

    var entity = document.Declarations.Single();
    Assert.Equal(3, entity.Span.Start.Line);
    Assert.Equal(3, entity.Span.Start.Column);
  }
}

public class ParseErrorTests
{
  public static TheoryData<string> InvalidDocuments => new()
    {
        "entity",
        "entity Person",
        "entity Person {",
        "entity Person { properties { name : } }",
        "entity Person { lifecycle { initial: } }",
        "relationship Employment between a : Foo { }",
        "archetype Broken { transitions { go : A -> } }",
        "unexpected Person { }",
        "entity Person { properties { name : String [ bogus ] } }",
    };

  [Theory]
  [MemberData(nameof(InvalidDocuments))]
  public void InvalidDocumentThrows(string source)
  {
    Assert.Throws<EidosParseException>(() => EidosGrammarParser.Parse(source));
  }

  [Fact]
  public void ParseExceptionCarriesSpan()
  {
    var exception = Assert.Throws<EidosParseException>(
        () => EidosGrammarParser.Parse("entity Person {\n  bogus\n}"));

    Assert.Equal(2, exception.Span.Start.Line);
  }
}
