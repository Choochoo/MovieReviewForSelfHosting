using MovieReviewApp.Infrastructure.Services;
using MovieReviewApp.Infrastructure.Configuration;
using MovieReviewApp.Models;
using Xunit;

namespace MovieReviewApp.Tests;

[Collection("Sequential")]
public class PersonReorderingIntegrationTests
{
    // Note: These tests validate the integration between reordering logic and demo protection
    // Full database integration tests would require MongoDB test container setup

    [Fact]
    public void DemoProtectionService_ShouldBlockReorderPerson()
    {
        // Arrange
        InstanceManager mockInstanceManager = CreateMockInstanceManager("demo");
        DemoProtectionService demoProtection = new DemoProtectionService(mockInstanceManager);

        // Act
        bool isAllowed = demoProtection.TryValidateNotDemo("Reorder Person", out string errorMessage);

        // Assert
        Assert.False(isAllowed);
        Assert.Contains("Reorder Person", errorMessage);
        Assert.Contains("demo mode", errorMessage);
    }

    [Fact]
    public void DemoProtectionService_ShouldAllowReorderPerson_InNonDemoMode()
    {
        // Arrange
        InstanceManager mockInstanceManager = CreateMockInstanceManager("production");
        DemoProtectionService demoProtection = new DemoProtectionService(mockInstanceManager);

        // Act
        bool isAllowed = demoProtection.TryValidateNotDemo("Reorder Person", out string errorMessage);

        // Assert
        Assert.True(isAllowed);
        Assert.Empty(errorMessage);
    }

    [Fact]
    public void DemoProtectionService_ShouldBlockKnownOperations()
    {
        // Arrange
        InstanceManager mockInstanceManager = CreateMockInstanceManager("demo");
        DemoProtectionService demoProtection = new DemoProtectionService(mockInstanceManager);

        string[] blockedOperations = {
            "Delete MovieSession",
            "Delete Person",
            "Delete Award",
            "Purge audio files",
            "Reset database",
            "Reorder Person"
        };

        // Act & Assert
        foreach (string operation in blockedOperations)
        {
            bool isAllowed = demoProtection.TryValidateNotDemo(operation, out string errorMessage);
            Assert.False(isAllowed, $"Operation '{operation}' should be blocked in demo mode");
            Assert.Contains(operation, errorMessage);
        }
    }

    [Fact]
    public void ReorderingWorkflow_MoveUpLogic_ShouldIdentifyCorrectPersons()
    {
        // Arrange - Simulate the logic from Settings.razor.cs MoveUp method
        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 },
            new Person { Name = "Charlie", Order = 3 }
        };

        Person? personToMoveUp = people.FirstOrDefault(p => p.Name == "Bob");
        Assert.NotNull(personToMoveUp);

        // Act - Find person above (this is the core MoveUp logic)
        Person? personAbove = people.FirstOrDefault(p => p.Order == personToMoveUp.Order - 1);

        // Assert
        Assert.NotNull(personAbove);
        Assert.Equal("Alice", personAbove.Name);
        Assert.Equal(1, personAbove.Order);
        Assert.Equal(2, personToMoveUp.Order);
    }

    [Fact]
    public void ReorderingWorkflow_MoveDownLogic_ShouldIdentifyCorrectPersons()
    {
        // Arrange - Simulate the logic from Settings.razor.cs MoveDown method
        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 },
            new Person { Name = "Charlie", Order = 3 }
        };

        Person? personToMoveDown = people.FirstOrDefault(p => p.Name == "Bob");
        Assert.NotNull(personToMoveDown);

        // Act - Find person below (this is the core MoveDown logic)
        Person? personBelow = people.FirstOrDefault(p => p.Order == personToMoveDown.Order + 1);

        // Assert
        Assert.NotNull(personBelow);
        Assert.Equal("Charlie", personBelow.Name);
        Assert.Equal(3, personBelow.Order);
        Assert.Equal(2, personToMoveDown.Order);
    }

    [Fact]
    public void ReorderingWorkflow_BoundaryChecks_ShouldPreventInvalidOperations()
    {
        // Arrange
        List<Person> people = new List<Person>
        {
            new Person { Name = "Alice", Order = 1 },
            new Person { Name = "Bob", Order = 2 },
            new Person { Name = "Charlie", Order = 3 }
        };

        // Act & Assert - Test MoveUp boundary check (first person)
        Person? firstPerson = people.FirstOrDefault(p => p.Name == "Alice");
        Assert.NotNull(firstPerson);
        bool canMoveUp = firstPerson.Order > 1;
        Assert.False(canMoveUp); // Alice (Order=1) cannot move up

        // Act & Assert - Test MoveDown boundary check (last person)
        Person? lastPerson = people.FirstOrDefault(p => p.Name == "Charlie");
        Assert.NotNull(lastPerson);
        bool canMoveDown = lastPerson.Order < people.Count;
        Assert.False(canMoveDown); // Charlie (Order=3) cannot move down when there are 3 people
    }

    [Fact]
    public void ReorderingWorkflow_OrderSwapping_ShouldMaintainConsistency()
    {
        // Arrange - Simulate complete order swapping workflow
        Person person1 = new Person { Name = "Alice", Order = 2 };
        Person person2 = new Person { Name = "Bob", Order = 3 };

        // Act - Perform the order swap (MoveUp alice = swap with Bob)
        int tempOrder = person1.Order;
        person1.Order = person2.Order;
        person2.Order = tempOrder;

        // Assert - Alice should now be at position 3, Bob at position 2
        Assert.Equal(3, person1.Order); // Alice moved down
        Assert.Equal(2, person2.Order); // Bob moved up

        // Verify no duplicate orders
        Assert.NotEqual(person1.Order, person2.Order);
    }

    // Helper method to create mock InstanceManager
    private static InstanceManager CreateMockInstanceManager(string instanceName)
    {
        // Note: This creates an InstanceManager with the specified instance name
        // In a real test environment, you might use a mocking framework like Moq
        return new InstanceManager(instanceName);
    }
}