using MovieReviewApp.Models;
using MovieReviewApp.Models.ViewModels;
using MovieReviewApp.Utilities;
using Xunit;

namespace MovieReviewApp.Tests;

[Collection("Sequential")]
public class HomePageDataServiceTests
{
    // Note: These are structure tests that verify the view model and service contract
    // Full integration tests would require a test database setup

    [Fact]
    public void HomePageViewModel_ShouldInitializeWithEmptyCollections()
    {
        // Arrange & Act
        HomePageViewModel viewModel = new HomePageViewModel();

        // Assert
        Assert.NotNull(viewModel.AllAwardEvents);
        Assert.Empty(viewModel.AllAwardEvents);
        
        Assert.NotNull(viewModel.AllAwardQuestions);
        Assert.Empty(viewModel.AllAwardQuestions);
        
        Assert.NotNull(viewModel.QuestionResults);
        Assert.Empty(viewModel.QuestionResults);
        
        Assert.NotNull(viewModel.DiscussionQuestions);
        Assert.Empty(viewModel.DiscussionQuestions);
        
        Assert.NotNull(viewModel.AllPeople);
        Assert.Empty(viewModel.AllPeople);
        
        Assert.NotNull(viewModel.Settings);
        Assert.Empty(viewModel.Settings);
        
        Assert.NotNull(viewModel.ExistingEvents);
        Assert.Empty(viewModel.ExistingEvents);
        
        Assert.NotNull(viewModel.DbPhases);
        Assert.Empty(viewModel.DbPhases);
        
        Assert.NotNull(viewModel.RecentUpdates);
        Assert.Empty(viewModel.RecentUpdates);
        
        Assert.NotNull(viewModel.AllNames);
        Assert.Empty(viewModel.AllNames);
        
        Assert.NotNull(viewModel.GeneratedPhases);
        Assert.Empty(viewModel.GeneratedPhases);
    }

    [Fact]
    public void HomePageViewModel_ShouldHaveCorrectDefaultValues()
    {
        // Arrange & Act
        HomePageViewModel viewModel = new HomePageViewModel();

        // Assert
        Assert.Null(viewModel.CurrentEvent);
        Assert.Null(viewModel.NextEvent);
        Assert.False(viewModel.IsShowingPastEvent);
        Assert.False(viewModel.IsCurrentPhaseAwardPhase);
        Assert.Null(viewModel.AwardSettings);
        Assert.Null(viewModel.StartDate);
        Assert.False(viewModel.RespectOrder);
    }

    [Fact]
    public void HomePageViewModel_ShouldAcceptDataAssignment()
    {
        // Arrange
        HomePageViewModel viewModel = new HomePageViewModel();
        DateTime testDate = new DateTime(2025, 7, 1);
        
        // Act
        viewModel.CurrentEvent = new MovieEvent { Person = "Test Person" };
        viewModel.StartDate = testDate;
        viewModel.RespectOrder = true;
        viewModel.IsCurrentPhaseAwardPhase = true;
        viewModel.AwardSettings = new AwardSetting { PhasesBeforeAward = 3 };
        viewModel.AllNames = new[] { "Person1", "Person2" };

        // Assert
        Assert.NotNull(viewModel.CurrentEvent);
        Assert.Equal("Test Person", viewModel.CurrentEvent.Person);
        Assert.Equal(testDate, viewModel.StartDate);
        Assert.True(viewModel.RespectOrder);
        Assert.True(viewModel.IsCurrentPhaseAwardPhase);
        Assert.NotNull(viewModel.AwardSettings);
        Assert.Equal(3, viewModel.AwardSettings.PhasesBeforeAward);
        Assert.Equal(2, viewModel.AllNames.Length);
    }

    [Fact]
    public void QuestionResults_Dictionary_ShouldWorkWithTupleKeys()
    {
        // Arrange
        HomePageViewModel viewModel = new HomePageViewModel();
        Guid awardEventId = Guid.NewGuid();
        Guid questionId = Guid.NewGuid();
        List<QuestionResult> results = new List<QuestionResult>
        {
            new QuestionResult { MovieTitle = "Test Movie", TotalPoints = 10 }
        };

        // Act
        viewModel.QuestionResults[(awardEventId, questionId)] = results;

        // Assert
        Assert.Single(viewModel.QuestionResults);
        Assert.True(viewModel.QuestionResults.ContainsKey((awardEventId, questionId)));
        Assert.Single(viewModel.QuestionResults[(awardEventId, questionId)]);
        Assert.Equal("Test Movie", viewModel.QuestionResults[(awardEventId, questionId)][0].MovieTitle);
    }
}