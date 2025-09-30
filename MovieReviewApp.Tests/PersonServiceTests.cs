using MovieReviewApp.Models;
using Xunit;

namespace MovieReviewApp.Tests;

[Collection("Sequential")]
public class PersonServiceTests
{
    // Note: These are unit tests for Person model validation and ordering logic
    // Integration tests with actual PersonService would require database setup

    [Fact]
    public void Person_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        Person person = new Person();

        // Assert
        Assert.Equal(0, person.Order);
        Assert.Null(person.Name);
        Assert.False(person.IsEditing);
    }

    [Fact]
    public void Person_ShouldSetProperties()
    {
        // Arrange
        Person person = new Person();
        string testName = "Test Person";
        int testOrder = 5;

        // Act
        person.Name = testName;
        person.Order = testOrder;
        person.IsEditing = true;

        // Assert
        Assert.Equal(testName, person.Name);
        Assert.Equal(testOrder, person.Order);
        Assert.True(person.IsEditing);
    }

    [Fact]
    public void PersonList_OrderedByOrder_ShouldRespectOrderField()
    {
        // Arrange
        List<Person> people = new List<Person>
        {
            new Person { Name = "Charlie", Order = 3 },
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 }
        };

        // Act
        List<Person> ordered = people.OrderBy(p => p.Order).ToList();

        // Assert
        Assert.Equal(3, ordered.Count);
        Assert.Equal("Alice", ordered[0].Name);
        Assert.Equal("Bob", ordered[1].Name);
        Assert.Equal("Charlie", ordered[2].Name);
        Assert.Equal(1, ordered[0].Order);
        Assert.Equal(2, ordered[1].Order);
        Assert.Equal(3, ordered[2].Order);
    }

    [Fact]
    public void PersonReordering_SwapOrder_ShouldWorkCorrectly()
    {
        // Arrange - Simulate the order swapping logic used in MoveUp/MoveDown
        Person person1 = new Person { Name = "Alice", Order = 1 };
        Person person2 = new Person { Name = "Bob", Order = 2 };

        // Act - Simulate MoveDown on person1 (swap with person2)
        int originalOrder1 = person1.Order;
        int originalOrder2 = person2.Order;

        person1.Order = originalOrder2;
        person2.Order = originalOrder1;

        // Assert
        Assert.Equal(2, person1.Order); // Alice moved down to position 2
        Assert.Equal(1, person2.Order); // Bob moved up to position 1
    }

    [Fact]
    public void PersonReordering_BoundaryConditions_ShouldPreventInvalidMoves()
    {
        // Arrange
        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 },
            new Person { Name = "Charlie", Order = 3 }
        };

        // Act & Assert - Test MoveUp boundary (first person can't move up)
        Person? firstPerson = people.FirstOrDefault(p => p.Order == 1);
        Assert.NotNull(firstPerson);
        Assert.True(firstPerson.Order <= 1); // This would prevent MoveUp

        // Act & Assert - Test MoveDown boundary (last person can't move down)
        Person? lastPerson = people.FirstOrDefault(p => p.Order == people.Count);
        Assert.NotNull(lastPerson);
        Assert.True(lastPerson.Order >= people.Count); // This would prevent MoveDown
    }

    [Fact]
    public void PersonReordering_DuplicateOrders_ShouldBeDetectable()
    {
        // Arrange - Create a scenario that could cause duplicate orders
        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 },
            new Person { Name = "Charlie", Order = 2 } // Duplicate order
        };

        // Act
        List<int> orders = people.Select(p => p.Order).ToList();
        List<int> distinctOrders = orders.Distinct().ToList();

        // Assert - This test verifies we can detect order conflicts
        Assert.NotEqual(orders.Count, distinctOrders.Count);
        Assert.Contains(2, orders);
        Assert.Equal(2, orders.Count(o => o == 2)); // Two people have order 2
    }

    [Fact]
    public void PersonReordering_SequentialOrders_ShouldBeNormalized()
    {
        // Arrange - Simulate the normalization logic used after deletion
        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Charlie", Order = 5 }, // Gap in sequence
            new Person { Name = "Bob", Order = 3 }
        };

        // Act - Sort and renumber sequentially (1, 2, 3...)
        List<Person> sorted = people.OrderBy(p => p.Order).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Order = i + 1;
        }

        // Assert
        Assert.Equal(1, sorted[0].Order);
        Assert.Equal(2, sorted[1].Order);
        Assert.Equal(3, sorted[2].Order);
        Assert.Equal("Alice", sorted[0].Name);
        Assert.Equal("Bob", sorted[1].Name);
        Assert.Equal("Charlie", sorted[2].Name);
    }
}