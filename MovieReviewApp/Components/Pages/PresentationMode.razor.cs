using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MovieReviewApp.Application.Services;
using MovieReviewApp.Models;

namespace MovieReviewApp.Components.Pages;

public partial class PresentationMode : ComponentBase
{
    [Inject]
    private DiscussionQuestionService QuestionService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private List<DiscussionQuestion> _questions = new();
    private int _currentIndex = -1;
    private bool _isLoaded = false;
    private ElementReference _container;

    private static readonly List<DiscussionQuestion> DefaultQuestions = new()
    {
        new DiscussionQuestion { Question = "Did I like the movie?", Order = 1 },
        new DiscussionQuestion { Question = "Am I glad I watched the movie?", Order = 2 },
        new DiscussionQuestion { Question = "Do I think I'd ever watch it again?", Order = 3 },
        new DiscussionQuestion { Question = "Would you ever recommend this movie?", Order = 4 },
        new DiscussionQuestion { Question = "What was my favorite part of the movie?", Order = 5 },
        new DiscussionQuestion { Question = "What was my least favorite part of the movie?", Order = 6 },
        new DiscussionQuestion { Question = "What was my favorite line of the movie?", Order = 7 },
    };

    protected override async Task OnInitializedAsync()
    {
        List<DiscussionQuestion> questions = await QuestionService.GetActiveQuestionsAsync();
        _questions = questions.Any() ? questions : DefaultQuestions;
        _isLoaded = true;
    }

    private void AdvanceQuestion()
    {
        if (_currentIndex > _questions.Count)
        {
            GoBack();
            return;
        }

        _currentIndex++;

        if (_currentIndex > _questions.Count)
        {
            Navigation.NavigateTo("/");
        }

        StateHasChanged();
    }

    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "ArrowRight" || e.Key == " " || e.Key == "Enter")
        {
            AdvanceQuestion();
        }
        else if (e.Key == "ArrowLeft" && _currentIndex > -1)
        {
            _currentIndex--;
            StateHasChanged();
        }
        else if (e.Key == "Escape")
        {
            GoBack();
        }
    }

    private void GoBack()
    {
        Navigation.NavigateTo("/");
    }
}
