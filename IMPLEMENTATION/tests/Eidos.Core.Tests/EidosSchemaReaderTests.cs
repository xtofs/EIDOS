using System.IO.Pipelines;
using System.Text;

namespace Eidos.Core.Tests;

public class EidosSchemaReaderTests
{
    private const string Source = """
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
        """;

    public static TheoryData<int> ChunkSizes => [1, 7, 64, 4096];

    [Theory]
    [MemberData(nameof(ChunkSizes))]
    public async Task ParsesFromPipeInChunks(int chunkSize)
    {
        var bytes = Encoding.UTF8.GetBytes(Source);
        var pipe = new Pipe();

        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, bytes.Length - offset);
            await pipe.Writer.WriteAsync(bytes.AsMemory(offset, length));
        }

        await pipe.Writer.CompleteAsync();

        var document = await EidosSchemaReader.ParseAsync(pipe.Reader);

        var archetype = Assert.IsType<ArchetypeDeclarationSyntax>(document.Declarations.Single());
        Assert.Equal("Activatable", archetype.Name);
        Assert.Equal(2, archetype.Lifecycle.Members.OfType<TransitionsBlockSyntax>().Single().Transitions.Count);
    }

    [Fact]
    public async Task ParsesEmptyInput()
    {
        var pipe = new Pipe();
        await pipe.Writer.CompleteAsync();

        var document = await EidosSchemaReader.ParseAsync(pipe.Reader);

        Assert.Empty(document.Declarations);
    }
}
