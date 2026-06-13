using System.Buffers;
using System.Globalization;

namespace Eidos.Core;

public sealed class EidosGrammarParser
{
    private readonly IReadOnlyList<EidosToken> _tokens;
    private int _index;

    private EidosGrammarParser(IReadOnlyList<EidosToken> tokens)
    {
        _tokens = tokens;
        _index = 0;
    }

    public static EidosDocumentSyntax Parse(ReadOnlySequence<char> input)
    {
        var scanner = new EidosScanner(input);
        var tokens = scanner.ScanTokens();
        var parser = new EidosGrammarParser(tokens);
        return parser.ParseDocument();
    }

    public static EidosDocumentSyntax Parse(string input)
    {
        return Parse(new ReadOnlySequence<char>(input.ToCharArray()));
    }

    private EidosDocumentSyntax ParseDocument()
    {
        var declarations = new List<TopDeclarationSyntax>();
        var start = CurrentSignificant().Span.Start;

        while (CurrentSignificant().Kind != EidosTokenKind.EndOfFile)
        {
            declarations.Add(ParseTopDeclaration());
        }

        var end = CurrentSignificant().Span.End;
        return new EidosDocumentSyntax(declarations, SourceSpan.From(start, end));
    }

    private TopDeclarationSyntax ParseTopDeclaration()
    {
        var start = CurrentSignificant().Span.Start;
        var annotations = ParseAnnotations();
        SkipTrivia();

        if (IsKeyword("entity"))
        {
            return ParseEntityDeclaration(annotations, start);
        }

        if (IsKeyword("relationship"))
        {
            return ParseRelationshipDeclaration(annotations, start);
        }

        if (IsKeyword("mixin"))
        {
            return ParseMixinDeclaration(annotations, start);
        }

        if (IsKeyword("archetype"))
        {
            return ParseArchetypeDeclaration(annotations, start);
        }

        throw Error("Expected top-level declaration");
    }

    private EntityDeclarationSyntax ParseEntityDeclaration(IReadOnlyList<AnnotationSyntax> annotations, SourcePosition start)
    {
        ExpectKeyword("entity");
        var name = ExpectTypeName();

        var mixins = new List<string>();
        if (TryConsumeKeyword("with"))
        {
            mixins.Add(ExpectTypeName());
            while (TryConsume(EidosTokenKind.Comma))
            {
                mixins.Add(ExpectTypeName());
            }
        }

        Expect(EidosTokenKind.LBrace, "Expected '{' to start entity body");
        var members = new List<EntityMemberSyntax>();
        while (!TryConsume(EidosTokenKind.RBrace))
        {
            SkipTrivia();
            if (Current().Kind == EidosTokenKind.RBrace)
            {
                Advance();
                break;
            }

            if (Current().Kind == EidosTokenKind.DocComment)
            {
                var doc = ParseDocComment();
                members.Add(new EntityDocCommentMemberSyntax(doc, doc.Span));
                continue;
            }

            if (IsKeyword("properties"))
            {
                var properties = ParsePropertiesBlock();
                members.Add(new EntityPropertiesMemberSyntax(properties, properties.Span));
                continue;
            }

            if (IsKeyword("lifecycle"))
            {
                var lifecycle = ParseLifecycleClause();
                members.Add(new EntityLifecycleMemberSyntax(lifecycle, lifecycle.Span));
                continue;
            }

            if (IsKeyword("url"))
            {
                var urlHint = ParseUrlHint();
                members.Add(new EntityUrlHintMemberSyntax(urlHint, urlHint.Span));
                continue;
            }

            throw Error("Unexpected entity member");
        }

        var end = Previous().Span.End;
        return new EntityDeclarationSyntax(name, mixins, members, annotations, SourceSpan.From(start, end));
    }

    private RelationshipDeclarationSyntax ParseRelationshipDeclaration(
        IReadOnlyList<AnnotationSyntax> annotations,
        SourcePosition start)
    {
        ExpectKeyword("relationship");
        var name = ExpectTypeName();
        ExpectKeyword("between");
        var participants = ParseParticipantList();

        Expect(EidosTokenKind.LBrace, "Expected '{' to start relationship body");
        var members = new List<RelationshipMemberSyntax>();
        while (!TryConsume(EidosTokenKind.RBrace))
        {
            SkipTrivia();
            if (Current().Kind == EidosTokenKind.RBrace)
            {
                Advance();
                break;
            }

            if (Current().Kind == EidosTokenKind.DocComment)
            {
                var doc = ParseDocComment();
                members.Add(new RelationshipDocCommentMemberSyntax(doc, doc.Span));
                continue;
            }

            if (IsKeyword("properties"))
            {
                var properties = ParsePropertiesBlock();
                members.Add(new RelationshipPropertiesMemberSyntax(properties, properties.Span));
                continue;
            }

            if (IsKeyword("lifecycle"))
            {
                var lifecycle = ParseLifecycleClause();
                members.Add(new RelationshipLifecycleMemberSyntax(lifecycle, lifecycle.Span));
                continue;
            }

            if (IsKeyword("coupling"))
            {
                var coupling = ParseCouplingBlock();
                members.Add(new RelationshipCouplingMemberSyntax(coupling, coupling.Span));
                continue;
            }

            if (IsKeyword("role"))
            {
                var role = ParseRoleDeclaration();
                members.Add(new RelationshipRoleMemberSyntax(role, role.Span));
                continue;
            }

            if (IsKeyword("url"))
            {
                var urlHint = ParseUrlHint();
                members.Add(new RelationshipUrlHintMemberSyntax(urlHint, urlHint.Span));
                continue;
            }

            throw Error("Unexpected relationship member");
        }

        var end = Previous().Span.End;
        return new RelationshipDeclarationSyntax(name, participants, members, annotations, SourceSpan.From(start, end));
    }

    private MixinDeclarationSyntax ParseMixinDeclaration(IReadOnlyList<AnnotationSyntax> annotations, SourcePosition start)
    {
        ExpectKeyword("mixin");
        var name = ExpectTypeName();
        Expect(EidosTokenKind.LBrace, "Expected '{' to start mixin body");

        var members = new List<MixinMemberSyntax>();
        while (!TryConsume(EidosTokenKind.RBrace))
        {
            SkipTrivia();
            if (Current().Kind == EidosTokenKind.RBrace)
            {
                Advance();
                break;
            }

            if (Current().Kind == EidosTokenKind.DocComment)
            {
                var doc = ParseDocComment();
                members.Add(new MixinDocCommentMemberSyntax(doc, doc.Span));
                continue;
            }

            if (IsKeyword("properties"))
            {
                var properties = ParsePropertiesBlock();
                members.Add(new MixinPropertiesMemberSyntax(properties, properties.Span));
                continue;
            }

            if (IsKeyword("lifecycle"))
            {
                var fragment = ParseLifecycleFragment();
                members.Add(new MixinLifecycleFragmentMemberSyntax(fragment, fragment.Span));
                continue;
            }

            throw Error("Unexpected mixin member");
        }

        var end = Previous().Span.End;
        return new MixinDeclarationSyntax(name, members, annotations, SourceSpan.From(start, end));
    }

    private ArchetypeDeclarationSyntax ParseArchetypeDeclaration(
        IReadOnlyList<AnnotationSyntax> annotations,
        SourcePosition start)
    {
        ExpectKeyword("archetype");
        var name = ExpectTypeName();
        var composable = TryConsumeKeyword("composable");
        Expect(EidosTokenKind.LBrace, "Expected '{' to start archetype lifecycle");
        var lifecycle = ParseInlineLifecycleUntil(EidosTokenKind.RBrace);
        var close = Expect(EidosTokenKind.RBrace, "Expected '}' after archetype lifecycle");
        var span = SourceSpan.From(start, close.Span.End);
        return new ArchetypeDeclarationSyntax(name, composable, lifecycle, annotations, span);
    }

    private List<ParticipantSyntax> ParseParticipantList()
    {
        var participants = new List<ParticipantSyntax>
        {
            ParseParticipant()
        };

        Expect(EidosTokenKind.Comma, "Expected ',' between participants");
        participants.Add(ParseParticipant());

        while (TryConsume(EidosTokenKind.Comma))
        {
            participants.Add(ParseParticipant());
        }

        return participants;
    }

    private ParticipantSyntax ParseParticipant()
    {
        var start = CurrentSignificant().Span.Start;
        var name = ExpectIdentifier();
        Expect(EidosTokenKind.Colon, "Expected ':' after participant name");
        var typeName = ExpectTypeName();

        var options = new List<ParticipantOptionSyntax>();
        if (TryConsume(EidosTokenKind.LBracket))
        {
            options.Add(ParseParticipantOption());
            while (TryConsume(EidosTokenKind.Comma))
            {
                options.Add(ParseParticipantOption());
            }

            Expect(EidosTokenKind.RBracket, "Expected ']' after participant options");
        }

        return new ParticipantSyntax(name, typeName, options, SourceSpan.From(start, Previous().Span.End));
    }

    private ParticipantOptionSyntax ParseParticipantOption()
    {
        var start = CurrentSignificant().Span.Start;

        if (TryConsumeKeyword("role"))
        {
            Expect(EidosTokenKind.Colon, "Expected ':' after role");
            var roleType = ExpectTypeName();
            return new ParticipantRoleOptionSyntax(roleType, SourceSpan.From(start, Previous().Span.End));
        }

        if (TryConsumeKeyword("multiplicity"))
        {
            Expect(EidosTokenKind.Colon, "Expected ':' after multiplicity");
            var multiplicity = ParseMultiplicity();
            return new ParticipantMultiplicityOptionSyntax(multiplicity, SourceSpan.From(start, Previous().Span.End));
        }

        throw Error("Expected participant option");
    }

    private MultiplicityKind ParseMultiplicity()
    {
        if (TryConsumeKeyword("one"))
        {
            return MultiplicityKind.One;
        }

        if (TryConsumeKeyword("many"))
        {
            return MultiplicityKind.Many;
        }

        if (TryConsumeKeyword("one-or-more"))
        {
            return MultiplicityKind.OneOrMore;
        }

        if (TryConsumeKeyword("zero-or-one"))
        {
            return MultiplicityKind.ZeroOrOne;
        }

        throw Error("Expected multiplicity value");
    }

    private PropertiesBlockSyntax ParsePropertiesBlock()
    {
        var start = CurrentSignificant().Span.Start;
        ExpectKeyword("properties");
        Expect(EidosTokenKind.LBrace, "Expected '{' to start properties block");

        var properties = new List<PropertyDeclarationSyntax>();
        while (!TryConsume(EidosTokenKind.RBrace))
        {
            SkipTrivia();
            if (Current().Kind == EidosTokenKind.RBrace)
            {
                Advance();
                break;
            }

            properties.Add(ParsePropertyDeclaration());
        }

        return new PropertiesBlockSyntax(properties, SourceSpan.From(start, Previous().Span.End));
    }

    private PropertyDeclarationSyntax ParsePropertyDeclaration()
    {
        var start = CurrentSignificant().Span.Start;
        var annotations = ParseAnnotations();
        var name = ExpectIdentifier();
        Expect(EidosTokenKind.Colon, "Expected ':' after property name");
        var type = ParseTypeExpression();

        var options = new List<PropertyOptionSyntax>();
        if (TryConsume(EidosTokenKind.LBracket))
        {
            options.Add(ParsePropertyOption());
            while (TryConsume(EidosTokenKind.Comma))
            {
                options.Add(ParsePropertyOption());
            }

            Expect(EidosTokenKind.RBracket, "Expected ']' after property options");
        }

        return new PropertyDeclarationSyntax(annotations, name, type, options, SourceSpan.From(start, Previous().Span.End));
    }

    private TypeExpressionSyntax ParseTypeExpression()
    {
        var start = CurrentSignificant().Span.Start;

        if (TryParseScalarType(out var scalarType))
        {
            return new ScalarTypeExpressionSyntax(scalarType, SourceSpan.From(start, Previous().Span.End));
        }

        if (TryConsumeKeyword("ref"))
        {
            var typeName = ExpectTypeName();
            return new ReferenceTypeExpressionSyntax(typeName, SourceSpan.From(start, Previous().Span.End));
        }

        if (TryConsumeKeyword("list"))
        {
            Expect(EidosTokenKind.Less, "Expected '<' after list");
            var elementType = ParseTypeExpression();
            var close = Expect(EidosTokenKind.Greater, "Expected '>' after list element type");
            return new ListTypeExpressionSyntax(elementType, SourceSpan.From(start, close.Span.End));
        }

        if (TryConsumeKeyword("optional"))
        {
            Expect(EidosTokenKind.Less, "Expected '<' after optional");
            var elementType = ParseTypeExpression();
            var close = Expect(EidosTokenKind.Greater, "Expected '>' after optional element type");
            return new OptionalTypeExpressionSyntax(elementType, SourceSpan.From(start, close.Span.End));
        }

        if (TryConsumeKeyword("enum"))
        {
            Expect(EidosTokenKind.LBracket, "Expected '[' after enum");
            var values = new List<string>
            {
                ExpectIdentifier()
            };

            while (TryConsume(EidosTokenKind.Comma))
            {
                values.Add(ExpectIdentifier());
            }

            var close = Expect(EidosTokenKind.RBracket, "Expected ']' after enum values");
            return new EnumTypeExpressionSyntax(values, SourceSpan.From(start, close.Span.End));
        }

        throw Error("Expected type expression");
    }

    private PropertyOptionSyntax ParsePropertyOption()
    {
        var start = CurrentSignificant().Span.Start;

        if (TryConsumeKeyword("required"))
        {
            return new PropertyFlagOptionSyntax(PropertyFlagKind.Required, SourceSpan.From(start, Previous().Span.End));
        }

        if (TryConsumeKeyword("unique"))
        {
            return new PropertyFlagOptionSyntax(PropertyFlagKind.Unique, SourceSpan.From(start, Previous().Span.End));
        }

        if (TryConsumeKeyword("readonly"))
        {
            return new PropertyFlagOptionSyntax(PropertyFlagKind.ReadOnly, SourceSpan.From(start, Previous().Span.End));
        }

        if (TryConsumeKeyword("writeonce"))
        {
            return new PropertyFlagOptionSyntax(PropertyFlagKind.WriteOnce, SourceSpan.From(start, Previous().Span.End));
        }

        if (TryConsumeKeyword("immutable"))
        {
            return new PropertyFlagOptionSyntax(PropertyFlagKind.Immutable, SourceSpan.From(start, Previous().Span.End));
        }

        if (TryConsumeKeyword("nullable"))
        {
            return new PropertyFlagOptionSyntax(PropertyFlagKind.Nullable, SourceSpan.From(start, Previous().Span.End));
        }

        if (TryConsumeKeyword("writable-in"))
        {
            Expect(EidosTokenKind.Colon, "Expected ':' after writable-in");
            var names = ParseTypeNameArray();
            return new WritableInOptionSyntax(names, SourceSpan.From(start, Previous().Span.End));
        }

        if (TryConsumeKeyword("visible-in"))
        {
            Expect(EidosTokenKind.Colon, "Expected ':' after visible-in");
            var names = ParseTypeNameArray();
            return new VisibleInOptionSyntax(names, SourceSpan.From(start, Previous().Span.End));
        }

        throw Error("Expected property option");
    }

    private LifecycleClauseSyntax ParseLifecycleClause()
    {
        var start = CurrentSignificant().Span.Start;
        ExpectKeyword("lifecycle");

        if (TryConsume(EidosTokenKind.Colon))
        {
            var archetypes = new List<string>
            {
                ExpectTypeName()
            };

            while (TryConsume(EidosTokenKind.Plus))
            {
                archetypes.Add(ExpectTypeName());
            }

            return new ArchetypeReferenceLifecycleSyntax(archetypes, SourceSpan.From(start, Previous().Span.End));
        }

        Expect(EidosTokenKind.LBrace, "Expected ':' or '{' after lifecycle");
        var lifecycle = ParseInlineLifecycleUntil(EidosTokenKind.RBrace);
        var close = Expect(EidosTokenKind.RBrace, "Expected '}' after lifecycle block");
        return new InlineLifecycleClauseSyntax(lifecycle, SourceSpan.From(start, close.Span.End));
    }

    private LifecycleFragmentSyntax ParseLifecycleFragment()
    {
        var start = CurrentSignificant().Span.Start;
        ExpectKeyword("lifecycle");
        Expect(EidosTokenKind.LBrace, "Expected '{' to start lifecycle fragment");
        var lifecycle = ParseInlineLifecycleUntil(EidosTokenKind.RBrace);
        var close = Expect(EidosTokenKind.RBrace, "Expected '}' after lifecycle fragment");
        return new LifecycleFragmentSyntax(lifecycle, SourceSpan.From(start, close.Span.End));
    }

    private InlineLifecycleSyntax ParseInlineLifecycleUntil(EidosTokenKind terminator)
    {
        var start = CurrentSignificant().Span.Start;
        var members = new List<LifecycleMemberSyntax>();

        while (CurrentSignificant().Kind != terminator)
        {
            if (IsKeyword("states"))
            {
                members.Add(ParseStatesBlock());
                continue;
            }

            if (IsKeyword("transitions"))
            {
                members.Add(ParseTransitionsBlock());
                continue;
            }

            if (IsKeyword("initial"))
            {
                members.Add(ParseInitialClause());
                continue;
            }

            throw Error("Unexpected lifecycle member");
        }

        var end = members.Count > 0 ? members[^1].Span.End : CurrentSignificant().Span.Start;
        return new InlineLifecycleSyntax(members, SourceSpan.From(start, end));
    }

    private InitialClauseSyntax ParseInitialClause()
    {
        var start = CurrentSignificant().Span.Start;
        ExpectKeyword("initial");
        Expect(EidosTokenKind.Colon, "Expected ':' after initial");
        var state = ExpectTypeName();
        return new InitialClauseSyntax(state, SourceSpan.From(start, Previous().Span.End));
    }

    private StatesBlockSyntax ParseStatesBlock()
    {
        var start = CurrentSignificant().Span.Start;
        ExpectKeyword("states");
        Expect(EidosTokenKind.LBrace, "Expected '{' to start states block");

        var states = new List<StateDeclarationSyntax>();
        while (!TryConsume(EidosTokenKind.RBrace))
        {
            SkipTrivia();
            if (Current().Kind == EidosTokenKind.RBrace)
            {
                Advance();
                break;
            }

            states.Add(ParseStateDeclaration());
        }

        return new StatesBlockSyntax(states, SourceSpan.From(start, Previous().Span.End));
    }

    private StateDeclarationSyntax ParseStateDeclaration()
    {
        var start = CurrentSignificant().Span.Start;
        var annotations = ParseAnnotations();
        var name = ExpectTypeName();
        string? label = null;
        if (CurrentSignificant().Kind == EidosTokenKind.StringLiteral)
        {
            label = Unquote(Advance().Lexeme);
        }

        return new StateDeclarationSyntax(annotations, name, label, SourceSpan.From(start, Previous().Span.End));
    }

    private TransitionsBlockSyntax ParseTransitionsBlock()
    {
        var start = CurrentSignificant().Span.Start;
        ExpectKeyword("transitions");
        Expect(EidosTokenKind.LBrace, "Expected '{' to start transitions block");

        var transitions = new List<TransitionDeclarationSyntax>();
        while (!TryConsume(EidosTokenKind.RBrace))
        {
            SkipTrivia();
            if (Current().Kind == EidosTokenKind.RBrace)
            {
                Advance();
                break;
            }

            transitions.Add(ParseTransitionDeclaration());
        }

        return new TransitionsBlockSyntax(transitions, SourceSpan.From(start, Previous().Span.End));
    }

    private TransitionDeclarationSyntax ParseTransitionDeclaration()
    {
        var start = CurrentSignificant().Span.Start;
        var annotations = ParseAnnotations();
        var name = ExpectIdentifier();
        Expect(EidosTokenKind.Colon, "Expected ':' after transition name");
        var stateSet = ParseStateSet();
        Expect(EidosTokenKind.Arrow, "Expected '->' in transition declaration");
        var target = ExpectTypeName();

        var options = new List<TransitionOptionSyntax>();
        if (TryConsume(EidosTokenKind.LBracket))
        {
            options.Add(ParseTransitionOption());
            while (TryConsume(EidosTokenKind.Comma))
            {
                options.Add(ParseTransitionOption());
            }

            Expect(EidosTokenKind.RBracket, "Expected ']' after transition options");
        }

        return new TransitionDeclarationSyntax(
            annotations,
            name,
            stateSet,
            target,
            options,
            SourceSpan.From(start, Previous().Span.End));
    }

    private StateSetSyntax ParseStateSet()
    {
        var start = CurrentSignificant().Span.Start;
        if (TryConsume(EidosTokenKind.LParen))
        {
            var names = new List<string>
            {
                ExpectTypeName()
            };

            Expect(EidosTokenKind.Pipe, "Expected '|' in multi-state set");
            names.Add(ExpectTypeName());

            while (TryConsume(EidosTokenKind.Pipe))
            {
                names.Add(ExpectTypeName());
            }

            var close = Expect(EidosTokenKind.RParen, "Expected ')' to close state set");
            return new MultiStateSetSyntax(names, SourceSpan.From(start, close.Span.End));
        }

        var name = ExpectTypeName();
        return new SingleStateSetSyntax(name, SourceSpan.From(start, Previous().Span.End));
    }

    private TransitionOptionSyntax ParseTransitionOption()
    {
        var start = CurrentSignificant().Span.Start;
        if (TryConsumeKeyword("guard"))
        {
            Expect(EidosTokenKind.Colon, "Expected ':' after guard");
            var guard = ParseGuardExpression();
            return new GuardTransitionOptionSyntax(guard, SourceSpan.From(start, Previous().Span.End));
        }

        if (TryConsumeKeyword("emits"))
        {
            Expect(EidosTokenKind.Colon, "Expected ':' after emits");
            var eventType = ExpectTypeName();
            return new EmitsTransitionOptionSyntax(eventType, SourceSpan.From(start, Previous().Span.End));
        }

        throw Error("Expected transition option");
    }

    private GuardExpressionSyntax ParseGuardExpression()
    {
        return ParseGuardOr();
    }

    private GuardExpressionSyntax ParseGuardOr()
    {
        var left = ParseGuardAnd();
        while (TryConsume(EidosTokenKind.OrOr))
        {
            var right = ParseGuardAnd();
            left = new GuardBinaryExpressionSyntax(
                GuardBinaryOperator.Or,
                left,
                right,
                SourceSpan.From(left.Span.Start, right.Span.End));
        }

        return left;
    }

    private GuardExpressionSyntax ParseGuardAnd()
    {
        var left = ParseGuardUnary();
        while (TryConsume(EidosTokenKind.AndAnd))
        {
            var right = ParseGuardUnary();
            left = new GuardBinaryExpressionSyntax(
                GuardBinaryOperator.And,
                left,
                right,
                SourceSpan.From(left.Span.Start, right.Span.End));
        }

        return left;
    }

    private GuardExpressionSyntax ParseGuardUnary()
    {
        var start = CurrentSignificant().Span.Start;
        if (TryConsume(EidosTokenKind.Bang))
        {
            var operand = ParseGuardUnary();
            return new GuardNotExpressionSyntax(operand, SourceSpan.From(start, operand.Span.End));
        }

        return ParseGuardPrimary();
    }

    private GuardExpressionSyntax ParseGuardPrimary()
    {
        var start = CurrentSignificant().Span.Start;

        if (TryConsume(EidosTokenKind.LParen))
        {
            var expression = ParseGuardExpression();
            var close = Expect(EidosTokenKind.RParen, "Expected ')' in guard expression");
            return new GuardParenthesizedExpressionSyntax(expression, SourceSpan.From(start, close.Span.End));
        }

        var name = ExpectIdentifier();
        if (TryConsume(EidosTokenKind.Dot))
        {
            ExpectKeyword("state");
            ExpectKeyword("in");
            var states = ParseTypeNameArray();
            return new ParticipantStateGuardSyntax(name, states, SourceSpan.From(start, Previous().Span.End));
        }

        return new GuardPredicateNameSyntax(name, SourceSpan.From(start, Previous().Span.End));
    }

    private CouplingBlockSyntax ParseCouplingBlock()
    {
        var start = CurrentSignificant().Span.Start;
        ExpectKeyword("coupling");
        Expect(EidosTokenKind.LBrace, "Expected '{' to start coupling block");

        var rules = new List<CouplingRuleSyntax>();
        while (!TryConsume(EidosTokenKind.RBrace))
        {
            SkipTrivia();
            if (Current().Kind == EidosTokenKind.RBrace)
            {
                Advance();
                break;
            }

            rules.Add(ParseCouplingRule());
        }

        return new CouplingBlockSyntax(rules, SourceSpan.From(start, Previous().Span.End));
    }

    private CouplingRuleSyntax ParseCouplingRule()
    {
        var start = CurrentSignificant().Span.Start;
        ExpectKeyword("on");
        var participant = ExpectIdentifier();
        Expect(EidosTokenKind.Dot, "Expected '.' after coupling participant name");
        ExpectKeyword("state");
        Expect(EidosTokenKind.Arrow, "Expected '->' in coupling rule");
        var targetState = ExpectTypeName();
        Expect(EidosTokenKind.Colon, "Expected ':' before transition call");
        var call = ParseTransitionCall();
        return new CouplingRuleSyntax(participant, targetState, call, SourceSpan.From(start, call.Span.End));
    }

    private TransitionCallSyntax ParseTransitionCall()
    {
        var start = CurrentSignificant().Span.Start;
        var name = ExpectIdentifier();
        var arguments = new List<CouplingArgumentSyntax>();
        if (TryConsume(EidosTokenKind.LParen))
        {
            arguments.Add(ParseCouplingArgument());
            while (TryConsume(EidosTokenKind.Comma))
            {
                arguments.Add(ParseCouplingArgument());
            }

            Expect(EidosTokenKind.RParen, "Expected ')' after coupling arguments");
        }

        return new TransitionCallSyntax(name, arguments, SourceSpan.From(start, Previous().Span.End));
    }

    private CouplingArgumentSyntax ParseCouplingArgument()
    {
        var start = CurrentSignificant().Span.Start;
        var name = ExpectIdentifier();
        Expect(EidosTokenKind.Colon, "Expected ':' in coupling argument");

        CouplingValueSyntax value;
        if (CurrentSignificant().Kind == EidosTokenKind.StringLiteral)
        {
            var token = Advance();
            value = new CouplingStringValueSyntax(Unquote(token.Lexeme), token.Span);
        }
        else
        {
            var tokenName = ExpectIdentifier();
            var span = Previous().Span;
            value = new CouplingNameValueSyntax(tokenName, span);
        }

        return new CouplingArgumentSyntax(name, value, SourceSpan.From(start, value.Span.End));
    }

    private RoleDeclarationSyntax ParseRoleDeclaration()
    {
        var start = CurrentSignificant().Span.Start;
        ExpectKeyword("role");
        var name = ExpectTypeName();
        Expect(EidosTokenKind.LBrace, "Expected '{' to start role block");

        var members = new List<RoleMemberSyntax>();
        while (!TryConsume(EidosTokenKind.RBrace))
        {
            SkipTrivia();
            if (Current().Kind == EidosTokenKind.RBrace)
            {
                Advance();
                break;
            }

            if (Current().Kind == EidosTokenKind.DocComment)
            {
                var doc = ParseDocComment();
                members.Add(new RoleDocCommentMemberSyntax(doc, doc.Span));
                continue;
            }

            if (IsKeyword("properties"))
            {
                var properties = ParsePropertiesBlock();
                members.Add(new RolePropertiesMemberSyntax(properties, properties.Span));
                continue;
            }

            if (IsKeyword("requires"))
            {
                var guard = ParseRoleGuardConstraint();
                members.Add(guard);
                continue;
            }

            throw Error("Unexpected role member");
        }

        return new RoleDeclarationSyntax(name, members, SourceSpan.From(start, Previous().Span.End));
    }

    private RoleGuardConstraintSyntax ParseRoleGuardConstraint()
    {
        var start = CurrentSignificant().Span.Start;
        ExpectKeyword("requires");
        Expect(EidosTokenKind.Colon, "Expected ':' after requires");
        var guard = ParseGuardExpression();
        return new RoleGuardConstraintSyntax(guard, SourceSpan.From(start, guard.Span.End));
    }

    private UrlHintSyntax ParseUrlHint()
    {
        var start = CurrentSignificant().Span.Start;
        ExpectKeyword("url");
        Expect(EidosTokenKind.Colon, "Expected ':' after url");
        var token = Expect(EidosTokenKind.StringLiteral, "Expected string literal for url hint");
        return new UrlHintSyntax(Unquote(token.Lexeme), SourceSpan.From(start, token.Span.End));
    }

    private DocCommentSyntax ParseDocComment()
    {
        var token = Expect(EidosTokenKind.DocComment, "Expected doc comment");
        var text = token.Lexeme.Length >= 6 ? token.Lexeme[3..^3] : string.Empty;
        return new DocCommentSyntax(text, token.Span);
    }

    private List<AnnotationSyntax> ParseAnnotations()
    {
        var annotations = new List<AnnotationSyntax>();
        while (CurrentSignificant().Kind == EidosTokenKind.At)
        {
            annotations.Add(ParseAnnotation());
        }

        return annotations;
    }

    private AnnotationSyntax ParseAnnotation()
    {
        var start = CurrentSignificant().Span.Start;
        Expect(EidosTokenKind.At, "Expected '@'");
        var name = ExpectIdentifier();
        var args = new List<AnnotationArgumentSyntax>();

        if (TryConsume(EidosTokenKind.LParen))
        {
            args.Add(ParseAnnotationArgument());
            while (TryConsume(EidosTokenKind.Comma))
            {
                args.Add(ParseAnnotationArgument());
            }

            Expect(EidosTokenKind.RParen, "Expected ')' after annotation arguments");
        }

        return new AnnotationSyntax(name, args, SourceSpan.From(start, Previous().Span.End));
    }

    private AnnotationArgumentSyntax ParseAnnotationArgument()
    {
        var start = CurrentSignificant().Span.Start;
        var name = ExpectIdentifier();
        Expect(EidosTokenKind.Colon, "Expected ':' after annotation argument name");
        var value = ParseAnnotationValue();
        return new AnnotationArgumentSyntax(name, value, SourceSpan.From(start, value.Span.End));
    }

    private AnnotationValueSyntax ParseAnnotationValue()
    {
        var token = CurrentSignificant();
        if (token.Kind == EidosTokenKind.StringLiteral)
        {
            var valueToken = Advance();
            return new AnnotationStringValueSyntax(Unquote(valueToken.Lexeme), valueToken.Span);
        }

        if (token.Kind == EidosTokenKind.IntegerLiteral)
        {
            var intToken = Advance();
            var value = int.Parse(intToken.Lexeme, NumberStyles.None, CultureInfo.InvariantCulture);
            return new AnnotationIntegerValueSyntax(value, intToken.Span);
        }

        var nameToken = Expect(EidosTokenKind.Identifier, "Expected annotation argument value");
        return new AnnotationNameValueSyntax(nameToken.Lexeme, nameToken.Span);
    }

    private List<string> ParseTypeNameArray()
    {
        Expect(EidosTokenKind.LBracket, "Expected '['");
        var names = new List<string>
        {
            ExpectTypeName()
        };

        while (TryConsume(EidosTokenKind.Comma))
        {
            names.Add(ExpectTypeName());
        }

        Expect(EidosTokenKind.RBracket, "Expected ']' after list");
        return names;
    }

    private bool TryParseScalarType(out ScalarTypeKind scalarType)
    {
        scalarType = default;
        var scalar = CurrentSignificant();
        if (scalar.Kind != EidosTokenKind.Identifier)
        {
            return false;
        }

        switch (scalar.Lexeme)
        {
            case "String":
                Advance();
                scalarType = ScalarTypeKind.String;
                return true;
            case "Integer":
                Advance();
                scalarType = ScalarTypeKind.Integer;
                return true;
            case "Number":
                Advance();
                scalarType = ScalarTypeKind.Number;
                return true;
            case "Boolean":
                Advance();
                scalarType = ScalarTypeKind.Boolean;
                return true;
            case "Date":
                Advance();
                scalarType = ScalarTypeKind.Date;
                return true;
            case "DateTime":
                Advance();
                scalarType = ScalarTypeKind.DateTime;
                return true;
            case "Time":
                Advance();
                scalarType = ScalarTypeKind.Time;
                return true;
            case "Money":
                Advance();
                scalarType = ScalarTypeKind.Money;
                return true;
            case "Email":
                Advance();
                scalarType = ScalarTypeKind.Email;
                return true;
            case "Url":
                Advance();
                scalarType = ScalarTypeKind.Url;
                return true;
            case "Uuid":
                Advance();
                scalarType = ScalarTypeKind.Uuid;
                return true;
            default:
                return false;
        }
    }

    private bool IsKeyword(string keyword)
    {
        var token = CurrentSignificant();
        return token.Kind == EidosTokenKind.Identifier && token.Lexeme.Equals(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryConsumeKeyword(string keyword)
    {
        if (!IsKeyword(keyword))
        {
            return false;
        }

        Advance();
        return true;
    }

    private void ExpectKeyword(string keyword)
    {
        var token = CurrentSignificant();
        if (token.Kind != EidosTokenKind.Identifier || !token.Lexeme.Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            throw new EidosParseException($"Expected keyword '{keyword}'", token.Span);
        }

        Advance();
    }

    private string ExpectIdentifier()
    {
        var token = Expect(EidosTokenKind.Identifier, "Expected identifier");
        return token.Lexeme;
    }

    private string ExpectTypeName()
    {
        var token = Expect(EidosTokenKind.Identifier, "Expected type name");
        return token.Lexeme;
    }

    private EidosToken Expect(EidosTokenKind kind, string message)
    {
        var token = CurrentSignificant();
        if (token.Kind != kind)
        {
            throw new EidosParseException(message, token.Span);
        }

        Advance();
        return token;
    }

    private bool TryConsume(EidosTokenKind kind)
    {
        if (CurrentSignificant().Kind != kind)
        {
            return false;
        }

        Advance();
        return true;
    }

    private EidosToken CurrentSignificant()
    {
        SkipTrivia();
        return Current();
    }

    private void SkipTrivia()
    {
        while (Current().Kind is EidosTokenKind.Whitespace or EidosTokenKind.LineComment or EidosTokenKind.BlockComment)
        {
            Advance();
        }
    }

    private EidosToken Current()
    {
        return _tokens[_index];
    }

    private EidosToken Previous()
    {
        var target = _index - 1;
        if (target < 0)
        {
            return _tokens[0];
        }

        return _tokens[target];
    }

    private EidosToken Advance()
    {
        var token = Current();
        if (_index < _tokens.Count - 1)
        {
            _index++;
        }

        return token;
    }

    private EidosParseException Error(string message)
    {
        return new EidosParseException(message, CurrentSignificant().Span);
    }

    private static string Unquote(string literal)
    {
        if (literal.Length >= 2 && literal[0] == '"' && literal[^1] == '"')
        {
            return literal[1..^1];
        }

        return literal;
    }
}
