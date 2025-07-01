using Xunit;

namespace MovieReviewApp.Tests;

[CollectionDefinition("Sequential", DisableParallelization = true)]
public class SequentialTestCollection
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}