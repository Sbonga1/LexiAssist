using System.Text.Json;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LexiAssist.Tests;

public sealed class ScreeningScoringServiceTests
{
    [Fact]
    public void Application_registers_the_scoring_service_and_binds_scoring_configuration()
    {
        using var factory = new LexiAssistWebApplicationFactory();
        var service = factory.Services.GetRequiredService<IScreeningScoringService>();
        var test = CreateTest(Question(1, ScreeningQuestionType.MultipleChoice, correct: "a"));

        var result = service.ScoreTest(test, [Response(1, selected: "a")]);

        Assert.Equal(100m, result.Percentage);
        Assert.IsType<ScreeningScoringService>(service);
    }

    [Fact]
    public void Scores_correct_and_incorrect_objective_answers_using_question_weights()
    {
        var test = CreateTest(
            Question(1, ScreeningQuestionType.MultipleChoice, correct: "blue", weight: 2),
            Question(2, ScreeningQuestionType.Spelling, correct: "friend", weight: 3));

        var result = CreateService().ScoreTest(test,
        [
            Response(1, selected: "blue"),
            Response(2, text: "freind")
        ]);

        Assert.Equal(2m, result.RawScore);
        Assert.Equal(5m, result.AvailableScore);
        Assert.Equal(40m, result.Percentage);
        Assert.Equal(2, result.ScorableQuestionCount);
        Assert.Equal(new[] { "Skill area" }, result.FlaggedSkillAreas);
        Assert.Equal(false, result.QuestionScores.Single(score => score.QuestionId == 2).IsCorrect);
    }

    [Fact]
    public void Matches_trimmed_answers_case_insensitively_and_includes_accepted_aliases()
    {
        var test = CreateTest(Question(
            1,
            ScreeningQuestionType.TextResponse,
            correct: "First answer",
            acceptedAnswers: "[\"Another answer\",\"Third\"]"));

        var result = CreateService().ScoreTest(test,
        [
            Response(1, text: "  aNoThEr AnSwEr  ")
        ]);

        Assert.Equal(100m, result.Percentage);
        Assert.Equal(1m, result.RawScore);
        Assert.Empty(result.FlaggedSkillAreas);
    }

    [Fact]
    public void Choice_question_types_use_the_selected_answer_field()
    {
        var test = CreateTest(
            Question(1, ScreeningQuestionType.MultipleChoice, correct: "yes"),
            Question(2, ScreeningQuestionType.PhonologicalChoice, correct: "rhyme"));

        var result = CreateService().ScoreTest(test,
        [
            Response(1, selected: "yes", text: "wrong"),
            Response(2, selected: "rhyme")
        ]);

        Assert.Equal(2m, result.RawScore);
        Assert.All(result.QuestionScores, score => Assert.True(score.IsCorrect));
    }

    [Fact]
    public void New_cognitive_activity_types_use_their_expected_answer_fields()
    {
        var test = CreateTest(
            Question(1, ScreeningQuestionType.WorkingMemorySequence, correct: "red blue green"),
            Question(2, ScreeningQuestionType.TimedMatching, correct: "same"),
            Question(3, ScreeningQuestionType.PatternReasoning, correct: "triangle"));

        var result = CreateService().ScoreTest(test,
        [
            Response(1, text: "red blue green"),
            Response(2, selected: "same"),
            Response(3, selected: "triangle")
        ]);

        Assert.Equal(3m, result.RawScore);
        Assert.Equal(3m, result.AvailableScore);
        Assert.Equal(100m, result.Percentage);
        Assert.All(result.QuestionScores, score => Assert.True(score.IsCorrect));
    }

    [Fact]
    public void Activity_metrics_calculate_neutral_measures_for_the_three_optional_areas()
    {
        var test = CreateTest(
            Question(
                1,
                ScreeningQuestionType.WorkingMemorySequence,
                correct: "red blue green",
                metadata: new ScreeningQuestionMetadata
                {
                    WorkingMemorySequence = ["red", "blue", "green"]
                }),
            Question(
                2,
                ScreeningQuestionType.TimedMatching,
                metadata: new ScreeningQuestionMetadata
                {
                    ChoiceOptions = ["●", "▲", "■"],
                    TimedMatchingReferenceMapping = new Dictionary<string, string>
                    {
                        ["A"] = "●",
                        ["B"] = "▲",
                        ["C"] = "■"
                    },
                    TimedMatchingItems = ["A", "B", "C", "A"]
                }),
            Question(3, ScreeningQuestionType.PatternReasoning, correct: "triangle"));

        var result = CreateService().ScoreTest(test,
        [
            Response(1, text: "red blue green"),
            new ScreeningResponse
            {
                ScreeningQuestionId = 2,
                ResponseTimeMilliseconds = 1200,
                ResponseDetailsJson = JsonSerializer.Serialize(new
                {
                    timeLimitSeconds = 30,
                    timeLimitReached = false,
                    attemptedCount = 4,
                    correctCount = 3,
                    incorrectCount = 1,
                    itemCount = 4
                })
            },
            Response(3, selected: "triangle")
        ]);

        var workingMemory = Assert.Single(result.ActivityMetrics,
            metric => metric.ActivityType == ScreeningQuestionType.WorkingMemorySequence);
        Assert.Equal(3, workingMemory.ItemsAttempted);
        Assert.Equal(3, workingMemory.ItemsCorrect);
        Assert.Equal(0, workingMemory.ItemsIncorrect);
        Assert.Equal(3, workingMemory.AvailableItemCount);
        Assert.Equal(100m, workingMemory.AccuracyPercentage);
        Assert.Equal(3, workingMemory.HighestCorrectlyCompletedSequenceLength);
        Assert.Equal(1m, workingMemory.AvailableScore);
        Assert.Equal(100m, workingMemory.Percentage);

        var processingSpeed = Assert.Single(result.ActivityMetrics,
            metric => metric.ActivityType == ScreeningQuestionType.TimedMatching);
        Assert.Equal(4, processingSpeed.ItemsAttempted);
        Assert.Equal(3, processingSpeed.ItemsCorrect);
        Assert.Equal(1, processingSpeed.ItemsIncorrect);
        Assert.Equal(4, processingSpeed.AvailableItemCount);
        Assert.Equal(75m, processingSpeed.AccuracyPercentage);
        Assert.Equal(1200L, processingSpeed.ElapsedTimeMilliseconds);
        Assert.Null(processingSpeed.AverageResponseTimeMilliseconds);
        Assert.Null(processingSpeed.MedianResponseTimeMilliseconds);
        Assert.Equal(3m, processingSpeed.RawCorrectCount);
        Assert.Equal(75m, processingSpeed.Percentage);

        var patternReasoning = Assert.Single(result.ActivityMetrics,
            metric => metric.ActivityType == ScreeningQuestionType.PatternReasoning);
        Assert.Equal(1m, patternReasoning.RawCorrectCount);
        Assert.Equal(1m, patternReasoning.AvailableScore);
        Assert.Equal(100m, patternReasoning.Percentage);
    }

    [Fact]
    public void Activity_metrics_keep_boundaries_and_respect_question_weighting()
    {
        var test = CreateTest(
            Question(
                1,
                ScreeningQuestionType.WorkingMemorySequence,
                correct: "red blue",
                metadata: new ScreeningQuestionMetadata
                {
                    WorkingMemorySequence = ["red", "blue"]
                },
                weight: 3),
            Question(
                2,
                ScreeningQuestionType.TimedMatching,
                metadata: new ScreeningQuestionMetadata
                {
                    ChoiceOptions = ["●", "▲"],
                    TimedMatchingReferenceMapping = new Dictionary<string, string>
                    {
                        ["A"] = "●",
                        ["B"] = "▲"
                    },
                    TimedMatchingItems = ["A", "B", "A", "B"]
                },
                weight: 2),
            Question(3, ScreeningQuestionType.PatternReasoning, correct: "triangle", weight: 4));

        var result = CreateService().ScoreTest(test,
        [
            Response(1, text: "blue red"),
            new ScreeningResponse
            {
                ScreeningQuestionId = 2,
                ResponseTimeMilliseconds = 900,
                ResponseDetailsJson = JsonSerializer.Serialize(new
                {
                    attemptedCount = 4,
                    correctCount = 0,
                    incorrectCount = 4,
                    itemCount = 4
                })
            },
            Response(3, selected: "square")
        ]);

        Assert.Equal(0m, result.Percentage);
        Assert.Equal(9m, result.AvailableScore);
        Assert.All(result.ActivityMetrics, metric =>
        {
            Assert.InRange(metric.AccuracyPercentage ?? 0m, 0m, 100m);
            Assert.InRange(metric.Percentage ?? 0m, 0m, 100m);
        });
        Assert.Equal(3m, result.ActivityMetrics.Single(metric =>
            metric.ActivityType == ScreeningQuestionType.WorkingMemorySequence).AvailableScore);
        Assert.Equal(2m, result.ActivityMetrics.Single(metric =>
            metric.ActivityType == ScreeningQuestionType.TimedMatching).AvailableScore);
        Assert.Equal(4m, result.ActivityMetrics.Single(metric =>
            metric.ActivityType == ScreeningQuestionType.PatternReasoning).AvailableScore);
    }

    [Fact]
    public void Activity_metrics_leave_unsupported_values_null_when_responses_are_missing()
    {
        var test = CreateTest(
            Question(
                1,
                ScreeningQuestionType.WorkingMemorySequence,
                correct: "red blue",
                metadata: new ScreeningQuestionMetadata
                {
                    WorkingMemorySequence = ["red", "blue"]
                }),
            Question(
                2,
                ScreeningQuestionType.TimedMatching,
                metadata: new ScreeningQuestionMetadata
                {
                    ChoiceOptions = ["●", "▲"],
                    TimedMatchingReferenceMapping = new Dictionary<string, string>
                    {
                        ["A"] = "●",
                        ["B"] = "▲"
                    },
                    TimedMatchingItems = ["A", "B"]
                }),
            Question(3, ScreeningQuestionType.PatternReasoning, correct: "triangle"));

        var result = CreateService().ScoreTest(test, []);

        var workingMemory = result.ActivityMetrics.Single(metric =>
            metric.ActivityType == ScreeningQuestionType.WorkingMemorySequence);
        Assert.Equal(0, workingMemory.ItemsAttempted);
        Assert.Equal(0, workingMemory.ItemsCorrect);
        Assert.Null(workingMemory.AccuracyPercentage);
        Assert.Null(workingMemory.HighestCorrectlyCompletedSequenceLength);

        var processingSpeed = result.ActivityMetrics.Single(metric =>
            metric.ActivityType == ScreeningQuestionType.TimedMatching);
        Assert.Equal(0, processingSpeed.ItemsAttempted);
        Assert.Equal(2, processingSpeed.AvailableItemCount);
        Assert.Null(processingSpeed.AccuracyPercentage);
        Assert.Null(processingSpeed.ElapsedTimeMilliseconds);
        Assert.Null(processingSpeed.AverageResponseTimeMilliseconds);
        Assert.Null(processingSpeed.MedianResponseTimeMilliseconds);

        var patternReasoning = result.ActivityMetrics.Single(metric =>
            metric.ActivityType == ScreeningQuestionType.PatternReasoning);
        Assert.Equal(0m, patternReasoning.RawCorrectCount);
        Assert.Equal(1m, patternReasoning.AvailableScore);
        Assert.Null(patternReasoning.Percentage);
    }

    [Fact]
    public void Unanswered_scorable_questions_stay_in_the_denominator_and_completion_is_incomplete()
    {
        var test = CreateTest(
            Question(1, ScreeningQuestionType.MultipleChoice, correct: "a"),
            Question(2, ScreeningQuestionType.Spelling, correct: "word"));

        var result = CreateService().ScoreTest(test, [Response(1, selected: "a")]);

        Assert.Equal(1m, result.RawScore);
        Assert.Equal(2m, result.AvailableScore);
        Assert.Equal(50m, result.Percentage);
        Assert.Equal(ScreeningCompletionStatus.InProgress, result.CompletionStatus);
        Assert.Equal(1, result.AnsweredQuestionCount);
        Assert.Equal(2, result.ExpectedQuestionCount);
        Assert.False(result.QuestionScores.Single(score => score.QuestionId == 2).HasResponse);
        Assert.Null(result.QuestionScores.Single(score => score.QuestionId == 2).IsCorrect);
    }

    [Fact]
    public void Missing_or_malformed_answer_keys_are_unscorable()
    {
        var test = CreateTest(
            Question(1, ScreeningQuestionType.TextResponse),
            Question(2, ScreeningQuestionType.TextResponse, acceptedAnswers: "not json"));

        var result = CreateService().ScoreTest(test,
        [
            Response(1, text: "response"),
            Response(2, text: "response")
        ]);

        Assert.Equal(0m, result.RawScore);
        Assert.Equal(0m, result.AvailableScore);
        Assert.Null(result.Percentage);
        Assert.Equal(0, result.ScorableQuestionCount);
        Assert.All(result.QuestionScores, score => Assert.False(score.IsScorable));
        Assert.Empty(result.FlaggedSkillAreas);
    }

    [Fact]
    public void Malformed_accepted_answers_can_fall_back_to_a_valid_correct_answer()
    {
        var test = CreateTest(Question(
            1,
            ScreeningQuestionType.Spelling,
            correct: "neighbour",
            acceptedAnswers: "{broken"));

        var result = CreateService().ScoreTest(test, [Response(1, text: "NEIGHBOUR")]);

        Assert.Equal(1m, result.RawScore);
        Assert.Equal(100m, result.Percentage);
    }

    [Fact]
    public void Inactive_questions_and_inactive_tests_are_excluded()
    {
        var test = CreateTest(
            Question(1, ScreeningQuestionType.MultipleChoice, correct: "a"),
            Question(2, ScreeningQuestionType.MultipleChoice, correct: "b", active: false));
        var service = CreateService();
        var activeTestResult = service.ScoreTest(test,
        [
            Response(1, selected: "a"),
            Response(2, selected: "b")
        ]);

        test.IsActive = false;
        var inactiveTestResult = service.ScoreTest(test, [Response(1, selected: "a")]);

        Assert.Equal(1, activeTestResult.ExpectedQuestionCount);
        Assert.Equal(1m, activeTestResult.AvailableScore);
        Assert.Equal(ScreeningCompletionStatus.Completed, activeTestResult.CompletionStatus);
        Assert.Equal(0, inactiveTestResult.ExpectedQuestionCount);
        Assert.Equal(0m, inactiveTestResult.AvailableScore);
        Assert.Null(inactiveTestResult.Percentage);
        Assert.Equal(ScreeningCompletionStatus.NotStarted, inactiveTestResult.CompletionStatus);
    }

    [Fact]
    public void Unknown_question_type_is_not_counted_as_a_question_that_must_be_completed()
    {
        var test = CreateTest(
            Question(1, ScreeningQuestionType.MultipleChoice, correct: "a"),
            Question(2, (ScreeningQuestionType)99, correct: "unknown"));

        var result = CreateService().ScoreTest(test, [Response(1, selected: "a")]);

        Assert.Equal(1, result.ExpectedQuestionCount);
        Assert.Equal(ScreeningCompletionStatus.Completed, result.CompletionStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.MaxValue)]
    public void Invalid_or_unrepresentable_weights_are_unscorable(double weight)
    {
        var test = CreateTest(Question(1, ScreeningQuestionType.MultipleChoice, correct: "a", weight: weight));

        var result = CreateService().ScoreTest(test, [Response(1, selected: "a")]);

        Assert.Equal(0m, result.AvailableScore);
        Assert.Equal(0, result.ScorableQuestionCount);
        Assert.Null(result.Percentage);
    }

    [Fact]
    public void Timing_and_fluency_metrics_are_not_scored_as_answer_key_correctness_by_default()
    {
        var test = CreateTest(
            Question(1, ScreeningQuestionType.TimedNaming, correct: "20/20"),
            Question(2, ScreeningQuestionType.ReadingFluency, correct: "60"));

        var result = CreateService().ScoreTest(test,
        [
            Response(1, text: "20/20"),
            Response(2, text: "60")
        ]);

        Assert.Equal(0m, result.AvailableScore);
        Assert.Equal(0, result.ScorableQuestionCount);
        Assert.Equal(ScreeningCompletionStatus.Completed, result.CompletionStatus);
        Assert.Null(result.Percentage);
    }

    [Fact]
    public void Configured_question_types_can_be_disabled()
    {
        var options = new ScreeningScoringOptions { ObjectiveQuestionTypes = [] };
        var test = CreateTest(Question(1, ScreeningQuestionType.MultipleChoice, correct: "a"));

        var result = CreateService(options).ScoreTest(test, [Response(1, selected: "a")]);

        Assert.Equal(0m, result.AvailableScore);
        Assert.Equal(0, result.ScorableQuestionCount);
    }

    [Fact]
    public void Completion_counts_unscorable_questions_that_have_been_presented_and_answered()
    {
        var test = CreateTest(
            Question(1, ScreeningQuestionType.MultipleChoice, correct: "a"),
            Question(2, ScreeningQuestionType.TextResponse));

        var result = CreateService().ScoreTest(test,
        [
            Response(1, selected: "a"),
            Response(2, text: "open response")
        ]);

        Assert.Equal(ScreeningCompletionStatus.Completed, result.CompletionStatus);
        Assert.Equal(2, result.AnsweredQuestionCount);
        Assert.Equal(2, result.ExpectedQuestionCount);
        Assert.Equal(1, result.ScorableQuestionCount);
        Assert.Equal(100m, result.Percentage);
    }

    [Fact]
    public void Configured_threshold_is_a_strict_review_cue_and_can_be_disabled()
    {
        var test = CreateTest(
            Question(1, ScreeningQuestionType.MultipleChoice, correct: "a"),
            Question(2, ScreeningQuestionType.MultipleChoice, correct: "b"));
        var responses = new[] { Response(1, selected: "a"), Response(2, selected: "wrong") };

        var thresholdResult = CreateService(new ScreeningScoringOptions
        {
            FlagSkillAreaBelowPercentage = 50m
        }).ScoreTest(test, responses);
        var disabledResult = CreateService(new ScreeningScoringOptions
        {
            FlagSkillAreaBelowPercentage = null
        }).ScoreTest(test, responses);

        Assert.Equal(50m, thresholdResult.Percentage);
        Assert.Empty(thresholdResult.FlaggedSkillAreas);
        Assert.Empty(disabledResult.FlaggedSkillAreas);

        var belowThreshold = CreateService(new ScreeningScoringOptions
        {
            FlagSkillAreaBelowPercentage = 51m
        }).ScoreTest(test, responses);
        Assert.Equal(new[] { "Skill area" }, belowThreshold.FlaggedSkillAreas);
    }

    [Fact]
    public void Overall_score_sums_points_instead_of_averaging_test_percentages()
    {
        var testOne = CreateTest(1, "One", Question(1, ScreeningQuestionType.MultipleChoice, correct: "a"));
        var testTwo = CreateTest(2, "Two",
            Question(2, ScreeningQuestionType.MultipleChoice, correct: "a", weight: 9));
        var service = CreateService();
        var testResults = new[]
        {
            service.ScoreTest(testOne, [Response(1, selected: "wrong")]),
            service.ScoreTest(testTwo, [Response(2, selected: "a")])
        };

        var result = service.ScoreOverall(testResults);

        Assert.Equal(9m, result.RawScore);
        Assert.Equal(10m, result.AvailableScore);
        Assert.Equal(90m, result.Percentage);
        Assert.Equal(ScreeningCompletionStatus.Completed, result.CompletionStatus);
        Assert.Equal(2, result.TestResults.Count);
        Assert.Equal(new[] { "One" }, result.FlaggedSkillAreas);
    }

    [Fact]
    public void Overall_result_is_ordered_and_flagged_skill_areas_are_distinct_and_sorted()
    {
        var service = CreateService();
        var later = new ScreeningTestScoringResult(
            9, "Zebra", "zebra", 0m, 1m, 0m, ["Zebra", "Shared"],
            ScreeningCompletionStatus.Completed, 1, 1, 1, []);
        var earlier = new ScreeningTestScoringResult(
            2, "Apple", "apple", 0m, 1m, 0m, ["Apple", "Shared"],
            ScreeningCompletionStatus.Completed, 1, 1, 1, []);

        var result = service.ScoreOverall([later, earlier]);

        Assert.Equal(new[] { 2, 9 }, result.TestResults.Select(test => test.ScreeningTestId));
        Assert.Equal(new[] { "Apple", "Shared", "Zebra" }, result.FlaggedSkillAreas);
    }

    [Fact]
    public void Overall_completion_is_in_progress_until_every_expected_question_is_answered()
    {
        var service = CreateService();
        var result = service.ScoreOverall(
        [
            new ScreeningTestScoringResult(
                1, "One", "one", 1m, 1m, 100m, [], ScreeningCompletionStatus.Completed, 1, 1, 1, []),
            new ScreeningTestScoringResult(
                2, "Two", "two", 0m, 1m, 0m, [], ScreeningCompletionStatus.InProgress, 0, 1, 1, [])
        ]);

        Assert.Equal(ScreeningCompletionStatus.InProgress, result.CompletionStatus);
        Assert.Equal(1, result.AnsweredQuestionCount);
        Assert.Equal(2, result.ExpectedQuestionCount);
    }

    [Fact]
    public void Duplicate_responses_for_a_question_are_rejected_as_ambiguous()
    {
        var test = CreateTest(Question(1, ScreeningQuestionType.MultipleChoice, correct: "a"));

        Assert.Throws<ArgumentException>(() => CreateService().ScoreTest(test,
        [
            Response(1, selected: "a"),
            Response(1, selected: "b")
        ]));
    }

    [Fact]
    public void Duplicate_test_results_are_rejected_when_combining_overall_result()
    {
        var service = CreateService();
        var result = service.ScoreTest(
            CreateTest(1, "One", Question(1, ScreeningQuestionType.MultipleChoice, correct: "a")),
            [Response(1, selected: "a")]);

        Assert.Throws<ArgumentException>(() => service.ScoreOverall([result, result]));
    }

    [Fact]
    public void Same_inputs_produce_identical_results_and_ordering()
    {
        var test = CreateTest(
            Question(3, ScreeningQuestionType.Spelling, correct: "cat", weight: 2),
            Question(1, ScreeningQuestionType.MultipleChoice, correct: "a"),
            Question(2, ScreeningQuestionType.TextResponse));
        var responses = new[]
        {
            Response(3, text: "cat"),
            Response(1, selected: "wrong"),
            Response(2, text: "unscorable")
        };
        var service = CreateService();

        var first = service.ScoreTest(test, responses);
        var second = service.ScoreTest(test, responses);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(new[] { 1, 2, 3 }, first.QuestionScores.Select(score => score.QuestionId));
    }

    [Fact]
    public void Invalid_review_threshold_fails_clearly()
    {
        var service = CreateService(new ScreeningScoringOptions { FlagSkillAreaBelowPercentage = 101m });
        var test = CreateTest(Question(1, ScreeningQuestionType.MultipleChoice, correct: "a"));

        Assert.Throws<InvalidOperationException>(() => service.ScoreTest(test, []));
    }

    private static ScreeningScoringService CreateService(ScreeningScoringOptions? options = null) =>
        new(Options.Create(options ?? new ScreeningScoringOptions()));

    private static ScreeningTest CreateTest(params ScreeningQuestion[] questions) => CreateTest(1, "Skill area", questions);

    private static ScreeningTest CreateTest(int id, string name, params ScreeningQuestion[] questions) => new()
    {
        Id = id,
        Name = name,
        Slug = name.ToLowerInvariant().Replace(' ', '-'),
        IsActive = true,
        Questions = questions
    };

    private static ScreeningQuestion Question(
        int id,
        ScreeningQuestionType type,
        string? correct = null,
        string? acceptedAnswers = null,
        double weight = 1,
        bool active = true,
        ScreeningQuestionMetadata? metadata = null) => new()
    {
        Id = id,
        QuestionType = type,
        CorrectAnswer = correct,
        AcceptedAnswersJson = acceptedAnswers,
        Weight = weight,
        IsActive = active,
        MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata)
    };

    private static ScreeningResponse Response(int questionId, string? selected = null, string? text = null) => new()
    {
        ScreeningQuestionId = questionId,
        SelectedAnswer = selected,
        TextAnswer = text
    };
}
