namespace Eidos.Core;

public abstract record EidosSyntaxNode(SourceSpan Span);

public sealed record EidosDocumentSyntax(IReadOnlyList<TopDeclarationSyntax> Declarations, SourceSpan Span)
    : EidosSyntaxNode(Span);

public sealed record AnnotationSyntax(string Name, IReadOnlyList<AnnotationArgumentSyntax> Arguments, SourceSpan Span)
    : EidosSyntaxNode(Span);

public sealed record AnnotationArgumentSyntax(string Name, AnnotationValueSyntax Value, SourceSpan Span)
    : EidosSyntaxNode(Span);

public abstract record AnnotationValueSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public sealed record AnnotationStringValueSyntax(string Value, SourceSpan Span) : AnnotationValueSyntax(Span);

public sealed record AnnotationNameValueSyntax(string Value, SourceSpan Span) : AnnotationValueSyntax(Span);

public sealed record AnnotationIntegerValueSyntax(int Value, SourceSpan Span) : AnnotationValueSyntax(Span);

public abstract record TopDeclarationSyntax(IReadOnlyList<AnnotationSyntax> Annotations, SourceSpan Span)
    : EidosSyntaxNode(Span);

public sealed record EntityDeclarationSyntax(
    string Name,
    IReadOnlyList<string> Mixins,
    IReadOnlyList<EntityMemberSyntax> Members,
    IReadOnlyList<AnnotationSyntax> Annotations,
    SourceSpan Span)
    : TopDeclarationSyntax(Annotations, Span);

public sealed record RelationshipDeclarationSyntax(
    string Name,
    IReadOnlyList<ParticipantSyntax> Participants,
    IReadOnlyList<RelationshipMemberSyntax> Members,
    IReadOnlyList<AnnotationSyntax> Annotations,
    SourceSpan Span)
    : TopDeclarationSyntax(Annotations, Span);

public sealed record MixinDeclarationSyntax(
    string Name,
    IReadOnlyList<MixinMemberSyntax> Members,
    IReadOnlyList<AnnotationSyntax> Annotations,
    SourceSpan Span)
    : TopDeclarationSyntax(Annotations, Span);

public sealed record ArchetypeDeclarationSyntax(
    string Name,
    bool IsComposable,
    InlineLifecycleSyntax Lifecycle,
    IReadOnlyList<AnnotationSyntax> Annotations,
    SourceSpan Span)
    : TopDeclarationSyntax(Annotations, Span);

public sealed record ParticipantSyntax(
    string Name,
    string TypeName,
    IReadOnlyList<ParticipantOptionSyntax> Options,
    SourceSpan Span)
    : EidosSyntaxNode(Span);

public abstract record ParticipantOptionSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public sealed record ParticipantRoleOptionSyntax(string RoleTypeName, SourceSpan Span)
    : ParticipantOptionSyntax(Span);

public enum MultiplicityKind
{
    One,
    Many,
    OneOrMore,
    ZeroOrOne
}

public sealed record ParticipantMultiplicityOptionSyntax(MultiplicityKind Multiplicity, SourceSpan Span)
    : ParticipantOptionSyntax(Span);

public abstract record DeclarationMemberSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public sealed record DocCommentSyntax(string Text, SourceSpan Span) : DeclarationMemberSyntax(Span);

public abstract record EntityMemberSyntax(SourceSpan Span) : DeclarationMemberSyntax(Span);

public abstract record RelationshipMemberSyntax(SourceSpan Span) : DeclarationMemberSyntax(Span);

public abstract record MixinMemberSyntax(SourceSpan Span) : DeclarationMemberSyntax(Span);

public sealed record EntityDocCommentMemberSyntax(DocCommentSyntax Comment, SourceSpan Span) : EntityMemberSyntax(Span);

public sealed record RelationshipDocCommentMemberSyntax(DocCommentSyntax Comment, SourceSpan Span) : RelationshipMemberSyntax(Span);

public sealed record MixinDocCommentMemberSyntax(DocCommentSyntax Comment, SourceSpan Span) : MixinMemberSyntax(Span);

public sealed record UrlHintSyntax(string Value, SourceSpan Span) : EidosSyntaxNode(Span);

public sealed record EntityUrlHintMemberSyntax(UrlHintSyntax UrlHint, SourceSpan Span) : EntityMemberSyntax(Span);

public sealed record RelationshipUrlHintMemberSyntax(UrlHintSyntax UrlHint, SourceSpan Span) : RelationshipMemberSyntax(Span);

public sealed record EntityPropertiesMemberSyntax(PropertiesBlockSyntax Properties, SourceSpan Span) : EntityMemberSyntax(Span);

public sealed record RelationshipPropertiesMemberSyntax(PropertiesBlockSyntax Properties, SourceSpan Span) : RelationshipMemberSyntax(Span);

public sealed record MixinPropertiesMemberSyntax(PropertiesBlockSyntax Properties, SourceSpan Span) : MixinMemberSyntax(Span);

public sealed record EntityLifecycleMemberSyntax(LifecycleClauseSyntax Lifecycle, SourceSpan Span) : EntityMemberSyntax(Span);

public sealed record RelationshipLifecycleMemberSyntax(LifecycleClauseSyntax Lifecycle, SourceSpan Span) : RelationshipMemberSyntax(Span);

public sealed record MixinLifecycleFragmentMemberSyntax(LifecycleFragmentSyntax Lifecycle, SourceSpan Span)
    : MixinMemberSyntax(Span);

public sealed record RelationshipCouplingMemberSyntax(CouplingBlockSyntax Coupling, SourceSpan Span) : RelationshipMemberSyntax(Span);

public sealed record RelationshipRoleMemberSyntax(RoleDeclarationSyntax Role, SourceSpan Span) : RelationshipMemberSyntax(Span);

public sealed record RoleDeclarationSyntax(string Name, IReadOnlyList<RoleMemberSyntax> Members, SourceSpan Span)
    : EidosSyntaxNode(Span);

public abstract record RoleMemberSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public sealed record RoleDocCommentMemberSyntax(DocCommentSyntax Comment, SourceSpan Span) : RoleMemberSyntax(Span);

public sealed record RolePropertiesMemberSyntax(PropertiesBlockSyntax Properties, SourceSpan Span) : RoleMemberSyntax(Span);

public sealed record RoleGuardConstraintSyntax(GuardExpressionSyntax Guard, SourceSpan Span) : RoleMemberSyntax(Span);

public sealed record PropertiesBlockSyntax(IReadOnlyList<PropertyDeclarationSyntax> Properties, SourceSpan Span)
    : EidosSyntaxNode(Span);

public sealed record PropertyDeclarationSyntax(
    IReadOnlyList<AnnotationSyntax> Annotations,
    string Name,
    TypeExpressionSyntax Type,
    IReadOnlyList<PropertyOptionSyntax> Options,
    SourceSpan Span)
    : EidosSyntaxNode(Span);

public abstract record TypeExpressionSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public enum ScalarTypeKind
{
    String,
    Integer,
    Number,
    Boolean,
    Date,
    DateTime,
    Time,
    Money,
    Email,
    Url,
    Uuid
}

public sealed record ScalarTypeExpressionSyntax(ScalarTypeKind Kind, SourceSpan Span) : TypeExpressionSyntax(Span);

public sealed record ReferenceTypeExpressionSyntax(string TypeName, SourceSpan Span) : TypeExpressionSyntax(Span);

public sealed record ListTypeExpressionSyntax(TypeExpressionSyntax ElementType, SourceSpan Span) : TypeExpressionSyntax(Span);

public sealed record OptionalTypeExpressionSyntax(TypeExpressionSyntax ElementType, SourceSpan Span)
    : TypeExpressionSyntax(Span);

public sealed record EnumTypeExpressionSyntax(IReadOnlyList<string> Values, SourceSpan Span) : TypeExpressionSyntax(Span);

public abstract record PropertyOptionSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public enum PropertyFlagKind
{
    Required,
    Unique,
    ReadOnly,
    WriteOnce,
    Immutable,
    Nullable
}

public sealed record PropertyFlagOptionSyntax(PropertyFlagKind Flag, SourceSpan Span) : PropertyOptionSyntax(Span);

public sealed record WritableInOptionSyntax(IReadOnlyList<string> StateNames, SourceSpan Span) : PropertyOptionSyntax(Span);

public sealed record VisibleInOptionSyntax(IReadOnlyList<string> StateNames, SourceSpan Span) : PropertyOptionSyntax(Span);

public abstract record LifecycleClauseSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public sealed record ArchetypeReferenceLifecycleSyntax(IReadOnlyList<string> Archetypes, SourceSpan Span)
    : LifecycleClauseSyntax(Span);

public sealed record InlineLifecycleClauseSyntax(InlineLifecycleSyntax Lifecycle, SourceSpan Span)
    : LifecycleClauseSyntax(Span);

public sealed record LifecycleFragmentSyntax(InlineLifecycleSyntax Lifecycle, SourceSpan Span) : EidosSyntaxNode(Span);

public sealed record InlineLifecycleSyntax(IReadOnlyList<LifecycleMemberSyntax> Members, SourceSpan Span)
    : EidosSyntaxNode(Span);

public abstract record LifecycleMemberSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public sealed record InitialClauseSyntax(string InitialState, SourceSpan Span) : LifecycleMemberSyntax(Span);

public sealed record StatesBlockSyntax(IReadOnlyList<StateDeclarationSyntax> States, SourceSpan Span)
    : LifecycleMemberSyntax(Span);

public sealed record StateDeclarationSyntax(
    IReadOnlyList<AnnotationSyntax> Annotations,
    string Name,
    string? Label,
    SourceSpan Span)
    : EidosSyntaxNode(Span);

public sealed record TransitionsBlockSyntax(IReadOnlyList<TransitionDeclarationSyntax> Transitions, SourceSpan Span)
    : LifecycleMemberSyntax(Span);

public sealed record TransitionDeclarationSyntax(
    IReadOnlyList<AnnotationSyntax> Annotations,
    string Name,
    StateSetSyntax SourceStates,
    string TargetState,
    IReadOnlyList<TransitionOptionSyntax> Options,
    SourceSpan Span)
    : EidosSyntaxNode(Span);

public abstract record StateSetSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public sealed record SingleStateSetSyntax(string StateName, SourceSpan Span) : StateSetSyntax(Span);

public sealed record MultiStateSetSyntax(IReadOnlyList<string> StateNames, SourceSpan Span) : StateSetSyntax(Span);

public abstract record TransitionOptionSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public sealed record GuardTransitionOptionSyntax(GuardExpressionSyntax Guard, SourceSpan Span)
    : TransitionOptionSyntax(Span);

public sealed record EmitsTransitionOptionSyntax(string EventTypeName, SourceSpan Span) : TransitionOptionSyntax(Span);

public abstract record GuardExpressionSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public enum GuardBinaryOperator
{
    And,
    Or
}

public sealed record GuardBinaryExpressionSyntax(
    GuardBinaryOperator Operator,
    GuardExpressionSyntax Left,
    GuardExpressionSyntax Right,
    SourceSpan Span)
    : GuardExpressionSyntax(Span);

public sealed record GuardNotExpressionSyntax(GuardExpressionSyntax Operand, SourceSpan Span)
    : GuardExpressionSyntax(Span);

public sealed record GuardPredicateNameSyntax(string Name, SourceSpan Span) : GuardExpressionSyntax(Span);

public sealed record ParticipantStateGuardSyntax(string ParticipantName, IReadOnlyList<string> AllowedStates, SourceSpan Span)
    : GuardExpressionSyntax(Span);

public sealed record GuardParenthesizedExpressionSyntax(GuardExpressionSyntax Expression, SourceSpan Span)
    : GuardExpressionSyntax(Span);

public sealed record CouplingBlockSyntax(IReadOnlyList<CouplingRuleSyntax> Rules, SourceSpan Span)
    : EidosSyntaxNode(Span);

public sealed record CouplingRuleSyntax(string ParticipantName, string TargetState, TransitionCallSyntax Call, SourceSpan Span)
    : EidosSyntaxNode(Span);

public sealed record TransitionCallSyntax(string Name, IReadOnlyList<CouplingArgumentSyntax> Arguments, SourceSpan Span)
    : EidosSyntaxNode(Span);

public sealed record CouplingArgumentSyntax(string Name, CouplingValueSyntax Value, SourceSpan Span)
    : EidosSyntaxNode(Span);

public abstract record CouplingValueSyntax(SourceSpan Span) : EidosSyntaxNode(Span);

public sealed record CouplingNameValueSyntax(string Name, SourceSpan Span) : CouplingValueSyntax(Span);

public sealed record CouplingStringValueSyntax(string Value, SourceSpan Span) : CouplingValueSyntax(Span);