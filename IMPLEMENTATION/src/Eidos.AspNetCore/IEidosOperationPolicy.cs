using Eidos.Core;

namespace Eidos.AspNetCore;

public interface IEidosOperationPolicy
{
    IReadOnlySet<EidosOperationType> RequiredForEntity(EntityDeclarationSyntax entity);

    IReadOnlySet<EidosOperationType> RequiredForRelationship(RelationshipDeclarationSyntax relationship);
}

public sealed class DefaultEidosOperationPolicy : IEidosOperationPolicy
{
    private static readonly IReadOnlySet<EidosOperationType> BaseOperations =
        new HashSet<EidosOperationType>
        {
            EidosOperationType.List,
            EidosOperationType.Get,
            EidosOperationType.Post
        };

    private static readonly IReadOnlySet<EidosOperationType> FullOperations =
        new HashSet<EidosOperationType>
        {
            EidosOperationType.List,
            EidosOperationType.Get,
            EidosOperationType.Post,
            EidosOperationType.PutState,
            EidosOperationType.Delete
        };

    public IReadOnlySet<EidosOperationType> RequiredForEntity(EntityDeclarationSyntax entity)
    {
        return HasLifecycle(entity.Members.OfType<EntityLifecycleMemberSyntax>().Select(x => x.Lifecycle))
            ? FullOperations
            : BaseOperations;
    }

    public IReadOnlySet<EidosOperationType> RequiredForRelationship(RelationshipDeclarationSyntax relationship)
    {
        return HasLifecycle(relationship.Members.OfType<RelationshipLifecycleMemberSyntax>().Select(x => x.Lifecycle))
            ? FullOperations
            : BaseOperations;
    }

    private static bool HasLifecycle(IEnumerable<LifecycleClauseSyntax> clauses)
    {
        foreach (var clause in clauses)
        {
            switch (clause)
            {
                case InlineLifecycleClauseSyntax:
                    return true;
                case ArchetypeReferenceLifecycleSyntax:
                    return true;
            }
        }

        return false;
    }
}
